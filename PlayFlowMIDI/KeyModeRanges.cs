namespace PlayFlowMIDI
{
    /// <summary>
    /// Single source for the MIDI note range each key layout can play.
    /// Shared by playback (MainWindow) and by the song analysis (MidiAnalyzer).
    /// </summary>
    public static class KeyModeRanges
    {
        public const string Mode37 = "37";
        public const string Mode36 = "36";
        public const string Mode15 = "15";

        /// <summary>The only notes the 15-key layout can play (two octaves of C major plus the top C).</summary>
        public static readonly int[] Mode15Notes = { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84 };

        /// <summary>
        /// Notes whose octave the 15-key auto-adapt (FoldTo15KeyScale) can still tell apart;
        /// anything outside is squeezed into the nearest of its three octaves.
        /// </summary>
        public const int Fold15InputMin = 48;
        public const int Fold15InputMax = 83;

        /// <summary>Lowest and highest playable note for a mode. Unknown modes use the 36-key range.</summary>
        public static (int Min, int Max) GetRange(string? mode) => mode switch
        {
            Mode37 => (48, 84),
            Mode15 => (Mode15Notes[0], Mode15Notes[^1]),
            _ => (48, 83),
        };

        /// <summary>True when the 15-key layout has a key for this exact note.</summary>
        public static bool IsMode15Note(int noteNumber) => Array.IndexOf(Mode15Notes, noteNumber) >= 0;
    }
}
