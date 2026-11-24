using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PlayFlowMIDI.Tests
{
    /// <summary>Builds small MIDI files on disk. 480 ticks per quarter at the default 120 BPM, so one quarter = 0.5 s.</summary>
    internal sealed class TestMidi : IDisposable
    {
        public const int Quarter = 480;

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "PlayFlowMIDI.Tests", Guid.NewGuid().ToString("N"));

        public TestMidi() => Directory.CreateDirectory(_folder);

        public string PathFor(string name) => Path.Combine(_folder, name);

        /// <summary>One track per array; every note is a quarter long and they play one after another.</summary>
        public string Write(string name, MidiFileFormat format, params (int Channel, int[] Notes)[] tracks)
        {
            var chunks = new List<TrackChunk>();
            foreach (var (channel, notes) in tracks)
            {
                var chunk = new TrackChunk();
                using (var manager = chunk.ManageNotes())
                {
                    for (int i = 0; i < notes.Length; i++)
                    {
                        manager.Objects.Add(new Note((SevenBitNumber)notes[i], Quarter, i * Quarter)
                        {
                            Channel = (FourBitNumber)channel
                        });
                    }
                }
                chunks.Add(chunk);
            }

            var file = new MidiFile(chunks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(Quarter) };
            string path = PathFor(name);
            file.Write(path, overwriteFile: true, format: format);
            return path;
        }

        public string Write(string name, params int[] notes) =>
            Write(name, MidiFileFormat.MultiTrack, (0, notes));

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); } catch { }
        }
    }
}
