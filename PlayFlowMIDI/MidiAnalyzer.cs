using System.IO;
using System.Text.Json.Serialization;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Per-song analysis, stored with the playlist in config.json so it is only recomputed
    /// when the file changes (size or modified time) or the algorithm version changes.
    /// </summary>
    public class MidiAnalysisData
    {
        /// <summary>0 = never analyzed. Compared against <see cref="MidiAnalyzer.CurrentVersion"/>.</summary>
        public int Version { get; set; } = 0;
        /// <summary>The file was missing or could not be read as MIDI.</summary>
        public bool Failed { get; set; } = false;
        public long FileSize { get; set; } = 0;
        public long FileModifiedUtcTicks { get; set; } = 0;

        public double DurationSeconds { get; set; } = 0;
        public int NoteCount { get; set; } = 0;
        /// <summary>Difficulty: notes per second over the whole file, percussion tracks excluded.</summary>
        public double NotesPerSecond { get; set; } = 0;
        /// <summary>Tracks that have notes, percussion tracks excluded.</summary>
        public int TrackCount { get; set; } = 0;
        public int MinNote { get; set; } = -1;
        public int MaxNote { get; set; } = -1;

        // Percentage (0-100) of notes each layout plays as written, with Pitch at 0 and
        // without relying on Wrap/Clamp. All layouts are computed in one pass so changing
        // the key mode or the profile never has to read the file again.
        public double Fit37 { get; set; } = 0;
        public double Fit36 { get; set; } = 0;
        /// <summary>15 keys, auto-adapt off: notes that are exactly one of the 15 playable notes.</summary>
        public double Fit15 { get; set; } = 0;
        /// <summary>
        /// 15 keys, auto-adapt on. The adapter folds every note onto the 15 keys, so "fits" here
        /// means the note survives musically: it belongs to the detected major key (no scale
        /// degree is substituted) and its octave is not squeezed (see KeyModeRanges.Fold15Input*).
        /// </summary>
        public double Fit15Adapted { get; set; } = 0;

        [JsonIgnore]
        public bool IsCurrent => Version == MidiAnalyzer.CurrentVersion;

        [JsonIgnore]
        public bool HasResult => IsCurrent && !Failed;

        public double GetFit(string? mode, bool autoAdapt15Keys) => mode switch
        {
            KeyModeRanges.Mode37 => Fit37,
            KeyModeRanges.Mode15 => autoAdapt15Keys ? Fit15Adapted : Fit15,
            _ => Fit36,
        };

        /// <summary>True when this analysis was made for the file as it is on disk now.</summary>
        public bool Matches(long fileSize, long fileModifiedUtcTicks) =>
            IsCurrent && FileSize == fileSize && FileModifiedUtcTicks == fileModifiedUtcTicks;
    }

    public sealed class MidiAnalysisResult
    {
        public MidiAnalysisData Data { get; init; } = new();
        /// <summary>Same text formats the playlist has always used ("120", "3:05").</summary>
        public string BPM { get; init; } = "N/A";
        public string Duration { get; init; } = "0:00";
    }

    /// <summary>Reads a MIDI file and measures how hard it is and how well it fits each key layout. No UI dependencies.</summary>
    public static class MidiAnalyzer
    {
        /// <summary>Bump when the calculation changes so cached results are recomputed.</summary>
        public const int CurrentVersion = 1;

        private const int PercussionChannel = 9;

        /// <summary>Size and modified time used as the cache key; (-1, 0) when the file is missing.</summary>
        public static (long Size, long ModifiedUtcTicks) GetFileStamp(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (info.Exists) return (info.Length, info.LastWriteTimeUtc.Ticks);
            }
            catch { }
            return (-1, 0);
        }

        /// <summary>Never throws: a missing or corrupted file returns a result with Data.Failed set.</summary>
        public static MidiAnalysisResult Analyze(string filePath)
        {
            var (size, modified) = GetFileStamp(filePath);
            try
            {
                if (size < 0) return Failure(size, modified);
                return AnalyzeFile(MidiFile.Read(filePath), size, modified);
            }
            catch
            {
                return Failure(size, modified);
            }
        }

        private static MidiAnalysisResult Failure(long size, long modified) => new()
        {
            Data = new MidiAnalysisData
            {
                Version = CurrentVersion,
                Failed = true,
                FileSize = size,
                FileModifiedUtcTicks = modified,
            }
        };

        private static MidiAnalysisResult AnalyzeFile(MidiFile midiFile, long size, long modified)
        {
            var tempoMap = midiFile.GetTempoMap();
            var bpm = tempoMap.GetTempoAtTime((MidiTimeSpan)0).BeatsPerMinute;
            var duration = midiFile.GetDuration<MetricTimeSpan>();
            double durationSeconds = duration.TotalMicroseconds / 1_000_000.0;

            // Same track view and percussion rule as playback: a track with any event on the
            // percussion channel is skipped as a whole. The "Disable percussion" setting and
            // per-song track choices are deliberately ignored so the result stays cacheable.
            var trackChunks = MainWindow.GetEffectiveTrackChunks(midiFile);
            var allNotes = new List<Note>();
            var playableNotes = new List<Note>();
            int trackCount = 0;

            foreach (var trackChunk in trackChunks)
            {
                var trackNotes = trackChunk.GetNotes();
                allNotes.AddRange(trackNotes);

                bool isPercussion = trackChunk.Events.OfType<ChannelEvent>().Any(e => e.Channel == PercussionChannel);
                if (isPercussion || trackNotes.Count == 0) continue;

                trackCount++;
                playableNotes.AddRange(trackNotes);
            }

            var data = new MidiAnalysisData
            {
                Version = CurrentVersion,
                FileSize = size,
                FileModifiedUtcTicks = modified,
                DurationSeconds = Math.Round(durationSeconds, 3),
                NoteCount = playableNotes.Count,
                TrackCount = trackCount,
            };

            if (playableNotes.Count > 0)
            {
                var (min37, max37) = KeyModeRanges.GetRange(KeyModeRanges.Mode37);
                var (min36, max36) = KeyModeRanges.GetRange(KeyModeRanges.Mode36);
                // Playback detects the key from every track (percussion channel notes excluded).
                int root = MainWindow.DetectMajorKey(allNotes);

                int minNote = int.MaxValue, maxNote = int.MinValue;
                int in37 = 0, in36 = 0, in15 = 0, in15Adapted = 0;

                foreach (var note in playableNotes)
                {
                    int n = note.NoteNumber;
                    if (n < minNote) minNote = n;
                    if (n > maxNote) maxNote = n;

                    if (n >= min37 && n <= max37) in37++;
                    if (n >= min36 && n <= max36) in36++;
                    if (KeyModeRanges.IsMode15Note(n)) in15++;
                    if (SurvivesFoldTo15Keys(n, root)) in15Adapted++;
                }

                double total = playableNotes.Count;
                data.MinNote = minNote;
                data.MaxNote = maxNote;
                data.NotesPerSecond = durationSeconds > 0 ? Math.Round(total / durationSeconds, 2) : 0;
                data.Fit37 = Percent(in37, total);
                data.Fit36 = Percent(in36, total);
                data.Fit15 = Percent(in15, total);
                data.Fit15Adapted = Percent(in15Adapted, total);
            }

            return new MidiAnalysisResult
            {
                Data = data,
                BPM = Math.Round(bpm).ToString(),
                Duration = $"{(int)duration.Minutes}:{duration.Seconds:D2}",
            };
        }

        private static double Percent(int count, double total) => Math.Round(count * 100.0 / total, 1);

        /// <summary>
        /// The note is in the major key rooted at <paramref name="root"/> (moved to C it lands on
        /// one of the 15-key notes) and within the octaves the fold keeps apart.
        /// </summary>
        private static bool SurvivesFoldTo15Keys(int noteNumber, int root)
        {
            if (noteNumber < KeyModeRanges.Fold15InputMin || noteNumber > KeyModeRanges.Fold15InputMax) return false;

            int degreeInC = ((noteNumber - root) % 12 + 12) % 12;
            return KeyModeRanges.IsMode15Note(KeyModeRanges.Mode15Notes[0] + degreeInC);
        }
    }
}
