using Melanchall.DryWetMidi.Core;

namespace PlayFlowMIDI.Tests
{
    public class MidiAnalyzerTests : IDisposable
    {
        private readonly TestMidi _midi = new();

        public void Dispose() => _midi.Dispose();

        [Fact]
        public void CountsNotesDurationAndRange()
        {
            // 8 quarter notes at 120 BPM = 4 seconds.
            string path = _midi.Write("scale.mid", 60, 62, 64, 65, 67, 69, 71, 72);

            var result = MidiAnalyzer.Analyze(path);
            var data = result.Data;

            Assert.True(data.HasResult);
            Assert.Equal(8, data.NoteCount);
            Assert.Equal(4.0, data.DurationSeconds, 2);
            Assert.Equal(2.0, data.NotesPerSecond, 2);
            Assert.Equal(1, data.TrackCount);
            Assert.Equal(60, data.MinNote);
            Assert.Equal(72, data.MaxNote);
            Assert.Equal("120", result.BPM);
            Assert.Equal("0:04", result.Duration);
        }

        [Fact]
        public void FitDependsOnTheKeyMode()
        {
            // 84 is the extra top key of the 37 layout; 47 is below every layout; 61 is a black key.
            string path = _midi.Write("edges.mid", 47, 60, 61, 84);

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.Equal(75.0, data.Fit37);
            Assert.Equal(50.0, data.Fit36);
            Assert.Equal(50.0, data.Fit15);
            Assert.Equal(data.Fit37, data.GetFit("37", false));
            Assert.Equal(data.Fit36, data.GetFit("36", false));
            Assert.Equal(data.Fit15, data.GetFit("15", false));
            Assert.Equal(data.Fit15Adapted, data.GetFit("15", true));
            Assert.Equal(data.Fit36, data.GetFit("", false));
            Assert.Equal(data.Fit36, data.GetFit(null, true));
        }

        [Fact]
        public void AdaptedFitFollowsTheDetectedKeyInsteadOfCMajor()
        {
            // G major scale: F#4 (66) is not a 15-key note, but it belongs to the detected key.
            string path = _midi.Write("gmajor.mid", 55, 57, 59, 60, 62, 64, 66, 67);

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.Equal(100.0, data.Fit15Adapted);
            Assert.True(data.Fit15 < 100.0);
        }

        [Fact]
        public void AdaptedFitDropsOutOfKeyAndOutOfOctaveNotes()
        {
            // C major scale plus one chromatic note (61) and one note below the fold range (36).
            string path = _midi.Write("cmajor-extra.mid", 60, 62, 64, 65, 67, 69, 71, 72, 61, 36);

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.Equal(80.0, data.Fit15Adapted);
        }

        [Fact]
        public void PercussionTracksAreIgnored()
        {
            string path = _midi.Write("drums.mid", MidiFileFormat.MultiTrack,
                (0, new[] { 60, 62, 64, 65 }),
                (9, new[] { 36, 38, 36, 38 }));

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.Equal(4, data.NoteCount);
            Assert.Equal(1, data.TrackCount);
            Assert.Equal(60, data.MinNote);
            Assert.Equal(100.0, data.Fit36);
        }

        [Fact]
        public void SingleTrackFilesAreSplitByChannel()
        {
            // Format 0 keeps everything in one track; playback splits it per channel.
            string path = _midi.Write("format0.mid", MidiFileFormat.SingleTrack,
                (0, new[] { 60, 62 }),
                (1, new[] { 48, 50 }),
                (9, new[] { 36, 38 }));

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.Equal(2, data.TrackCount);
            Assert.Equal(4, data.NoteCount);
            Assert.Equal(48, data.MinNote);
        }

        [Fact]
        public void TracksWithoutNotesAreNotCounted()
        {
            string path = _midi.Write("empty-track.mid", MidiFileFormat.MultiTrack,
                (0, Array.Empty<int>()),
                (0, new[] { 60 }));

            Assert.Equal(1, MidiAnalyzer.Analyze(path).Data.TrackCount);
        }

        [Fact]
        public void FileWithoutNotesDoesNotFail()
        {
            string path = _midi.Write("silent.mid");

            var data = MidiAnalyzer.Analyze(path).Data;

            Assert.True(data.HasResult);
            Assert.Equal(0, data.NoteCount);
            Assert.Equal(0, data.NotesPerSecond);
            Assert.Equal(-1, data.MinNote);
            Assert.Equal(0, data.Fit37);
        }

        [Fact]
        public void CorruptedFileIsReportedAsFailedWithoutThrowing()
        {
            string path = _midi.PathFor("broken.mid");
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x01, 0xFF, 0x13, 0x37 });

            var result = MidiAnalyzer.Analyze(path);

            Assert.True(result.Data.Failed);
            Assert.False(result.Data.HasResult);
            Assert.True(result.Data.IsCurrent);
            Assert.Equal(9, result.Data.FileSize);
            Assert.Equal("N/A", result.BPM);
            Assert.Equal("0:00", result.Duration);
        }

        [Fact]
        public void MissingFileIsReportedAsFailedWithoutThrowing()
        {
            var data = MidiAnalyzer.Analyze(_midi.PathFor("nope.mid")).Data;

            Assert.True(data.Failed);
            Assert.Equal(-1, data.FileSize);
        }

        [Fact]
        public void CacheMatchesOnlyTheSameFileStampAndVersion()
        {
            string path = _midi.Write("cache.mid", 60, 62);
            var data = MidiAnalyzer.Analyze(path).Data;
            var (size, modified) = MidiAnalyzer.GetFileStamp(path);

            Assert.True(data.Matches(size, modified));
            Assert.False(data.Matches(size + 1, modified));
            Assert.False(data.Matches(size, modified + 1));

            data.Version = MidiAnalyzer.CurrentVersion - 1;
            Assert.False(data.Matches(size, modified));
            Assert.False(new MidiAnalysisData().Matches(0, 0));
        }

        [Theory]
        [InlineData("37", 48, 84)]
        [InlineData("36", 48, 83)]
        [InlineData("15", 60, 84)]
        [InlineData("", 48, 83)]
        [InlineData(null, 48, 83)]
        public void KeyModeRangesMatchTheLayouts(string? mode, int min, int max)
        {
            Assert.Equal((min, max), KeyModeRanges.GetRange(mode));
        }
    }
}
