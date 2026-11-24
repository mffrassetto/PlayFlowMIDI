using System.ComponentModel;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Analysis side of a playlist entry. Kept apart from the MidiSong declaration in
    /// MainWindow.xaml.cs so that file changes as little as possible.
    /// </summary>
    public partial class MidiSong : INotifyPropertyChanged
    {
        private const string PendingText = "…";
        private const string UnavailableText = "–";

        public event PropertyChangedEventHandler? PropertyChanged;

        public MidiAnalysisData Analysis { get; set; } = new();

        /// <summary>Fit for the key mode currently selected; set through <see cref="UpdateFit"/>.</summary>
        public double FitPercent { get; private set; }

        public double NotesPerSecond => Analysis.HasResult ? Analysis.NotesPerSecond : 0;

        public string DifficultyText => AnalysisText(Analysis.NotesPerSecond.ToString("0.0"));

        public string FitText => AnalysisText(FitPercent.ToString("0") + "%");

        private string AnalysisText(string value) =>
            !Analysis.IsCurrent ? PendingText
            : Analysis.Failed || Analysis.NoteCount == 0 ? UnavailableText
            : value;

        /// <summary>Returns true when the displayed fit changed.</summary>
        public bool UpdateFit(string? mode, bool autoAdapt15Keys)
        {
            double fit = Analysis.HasResult ? Analysis.GetFit(mode, autoAdapt15Keys) : 0;
            if (fit == FitPercent) return false;

            FitPercent = fit;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FitText)));
            return true;
        }

        /// <summary>Stores a finished analysis and refreshes every bound column of this row.</summary>
        public void ApplyAnalysis(MidiAnalysisResult result, string? mode, bool autoAdapt15Keys)
        {
            Analysis = result.Data;
            if (!result.Data.Failed)
            {
                BPM = result.BPM;
                Duration = result.Duration;
            }
            FitPercent = Analysis.HasResult ? Analysis.GetFit(mode, autoAdapt15Keys) : 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
