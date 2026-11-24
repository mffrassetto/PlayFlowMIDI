using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Lets the user describe a playlist (length, difficulty, fit, tracks, order) and preview it.
    /// The selection itself is done by <see cref="PlaylistBuilder"/>; this window only edits the criteria.
    /// </summary>
    public partial class AutoPlaylistWindow : Window
    {
        private readonly List<MidiSong> _songs;
        private bool _updating = true;
        private int _shuffleSeed = Environment.TickCount;

        /// <summary>The criteria in use when Apply was pressed.</summary>
        public AutoPlaylistCriteria Criteria { get; private set; }

        /// <summary>The songs shown in the preview when Apply was pressed, in playing order.</summary>
        public List<MidiSong> Result { get; private set; } = new();

        /// <summary>Set when the user chose "Save as Playlist" instead of Apply: the name for the new playlist.</summary>
        public string? SaveAsPlaylistName { get; private set; }

        private readonly Func<string, string?>? _validatePlaylistName;

        /// <param name="validatePlaylistName">
        /// Returns null when a playlist name can be used, otherwise the message to show.
        /// Without it the window only offers Apply.
        /// </param>
        public AutoPlaylistWindow(IEnumerable<MidiSong> songs, AutoPlaylistCriteria criteria, string keyModeLabel,
            Func<string, string?>? validatePlaylistName = null)
        {
            InitializeComponent();

            _songs = songs.ToList();
            Criteria = criteria;
            _validatePlaylistName = validatePlaylistName;
            if (validatePlaylistName != null) SaveAsPlaylistButton.Visibility = Visibility.Visible;

            TargetMinutesBox.Text = FormatNumber(criteria.TargetMinutes);
            MinFitBox.Text = FormatNumber(criteria.MinFitPercent);
            MaxTracksBox.Text = FormatNumber(criteria.MaxTracks);
            OrderBox.SelectedIndex = criteria.Order == AutoPlaylistOrder.Random ? 1 : 0;
            ShuffleAgainButton.Visibility = criteria.Order == AutoPlaylistOrder.Random ? Visibility.Visible : Visibility.Collapsed;

            SelectPreset(criteria.DifficultyPreset);
            // Easy/Medium follow the library, so their number is recalculated instead of restored.
            MaxDifficultyBox.Text = FormatNumber(PresetLimit(SelectedPreset) ?? criteria.MaxNotesPerSecond);

            ModeText.Text = $"Fit % is measured for the key mode in use ({keyModeLabel}). Songs that are still being analyzed are left out.";

            _updating = false;
            UpdatePreview();
        }

        private string SelectedPreset =>
            (DifficultyPresetBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? AutoPlaylistCriteria.PresetAny;

        private void SelectPreset(string? preset)
        {
            foreach (ComboBoxItem item in DifficultyPresetBox.Items)
            {
                if (item.Tag?.ToString() == preset)
                {
                    DifficultyPresetBox.SelectedItem = item;
                    return;
                }
            }
            DifficultyPresetBox.SelectedIndex = 0;
        }

        /// <summary>The notes/s limit a preset stands for, or null when the preset leaves the number to the user.</summary>
        private double? PresetLimit(string preset) => preset switch
        {
            AutoPlaylistCriteria.PresetAny => 0,
            AutoPlaylistCriteria.PresetEasy => PlaylistBuilder.DifficultyAtFraction(_songs, 1.0 / 3),
            AutoPlaylistCriteria.PresetMedium => PlaylistBuilder.DifficultyAtFraction(_songs, 2.0 / 3),
            _ => null,
        };

        private void DifficultyPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating) return;

            double? limit = PresetLimit(SelectedPreset);
            if (limit.HasValue)
            {
                _updating = true;
                MaxDifficultyBox.Text = FormatNumber(limit.Value);
                _updating = false;
            }
            UpdatePreview();
        }

        private void MaxDifficulty_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;

            _updating = true;
            SelectPreset(AutoPlaylistCriteria.PresetCustom);
            _updating = false;
            UpdatePreview();
        }

        private void Criteria_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_updating) UpdatePreview();
        }

        private void Order_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating) return;

            ShuffleAgainButton.Visibility = OrderBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        private void ShuffleAgain_Click(object sender, RoutedEventArgs e)
        {
            _shuffleSeed++;
            UpdatePreview();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (Result.Count == 0) return;
            DialogResult = true;
        }

        private void SaveAsPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (Result.Count == 0 || _validatePlaylistName == null) return;

            var prompt = new TextPromptWindow("Save as Playlist", "Playlist name", string.Empty, _validatePlaylistName)
            {
                Owner = this,
                Topmost = Topmost,
            };
            if (prompt.ShowDialog() != true) return;

            SaveAsPlaylistName = prompt.Value;
            DialogResult = true;
        }

        private void UpdatePreview()
        {
            bool valid = TryReadNumber(TargetMinutesBox, out double minutes);
            valid &= TryReadNumber(MaxDifficultyBox, out double maxDifficulty);
            valid &= TryReadNumber(MinFitBox, out double minFit);
            valid &= TryReadNumber(MaxTracksBox, out double maxTracks);

            PreviewListView.Items.Clear();
            Result = new List<MidiSong>();

            if (!valid)
            {
                ShowEmpty("Check the highlighted values: only positive numbers are accepted.");
                SummaryText.Text = string.Empty;
                ApplyButton.IsEnabled = false;
                SaveAsPlaylistButton.IsEnabled = false;
                return;
            }

            Criteria = new AutoPlaylistCriteria
            {
                TargetMinutes = minutes,
                MaxNotesPerSecond = maxDifficulty,
                DifficultyPreset = SelectedPreset,
                MinFitPercent = minFit,
                MaxTracks = (int)Math.Min(maxTracks, int.MaxValue),
                Order = OrderBox.SelectedIndex == 1 ? AutoPlaylistOrder.Random : AutoPlaylistOrder.DifficultyAscending,
            };

            // The seed only changes with "Shuffle again", so typing does not reshuffle the preview.
            var result = PlaylistBuilder.Build(_songs, Criteria, new Random(_shuffleSeed));
            Result = result.Songs;
            foreach (var song in Result) PreviewListView.Items.Add(song);

            string pending = result.NotAnalyzedCount > 0 ? $" {result.NotAnalyzedCount} not analyzed." : string.Empty;

            if (Result.Count > 0)
            {
                EmptyText.Visibility = Visibility.Collapsed;
                var total = TimeSpan.FromSeconds(result.TotalSeconds);
                SummaryText.Text = $"{Result.Count} of {result.MatchingCount} matching songs, {(int)total.TotalMinutes}:{total.Seconds:D2} in total.{pending}";
            }
            else
            {
                SummaryText.Text = pending.Trim();
                if (_songs.Count == 0)
                    ShowEmpty("The playlist is empty. Load a folder or add songs first.");
                else if (result.NotAnalyzedCount == _songs.Count)
                    ShowEmpty("No song has been analyzed yet. Wait for the analysis to finish and try again.");
                else if (result.MatchingCount == 0)
                    ShowEmpty("No songs match these criteria. Try a lower Min Fit %, a higher difficulty or more tracks.");
                else
                    ShowEmpty("Songs match these criteria, but every one of them is longer than the total duration.");
            }

            ApplyButton.IsEnabled = Result.Count > 0;
            SaveAsPlaylistButton.IsEnabled = Result.Count > 0;
        }

        private void ShowEmpty(string message)
        {
            EmptyText.Text = message;
            EmptyText.Visibility = Visibility.Visible;
        }

        private static string FormatNumber(double value) =>
            value > 0 ? value.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;

        /// <summary>Empty means 0 ("no limit"). Accepts both "9,5" and "9.5". Marks the box when the text is not a number.</summary>
        private static bool TryReadNumber(System.Windows.Controls.TextBox box, out double value)
        {
            string text = box.Text.Trim();
            bool ok = true;
            value = 0;

            if (text.Length > 0)
            {
                ok = (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                     && value >= 0 && !double.IsInfinity(value);
                if (!ok) value = 0;
            }

            if (ok) box.ClearValue(BorderBrushProperty);
            else box.BorderBrush = System.Windows.Media.Brushes.Red;
            return ok;
        }
    }
}
