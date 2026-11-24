using System.Runtime.ExceptionServices;
using System.Windows.Controls;

namespace PlayFlowMIDI.Tests
{
    /// <summary>Builds the window off-screen (it is never shown) to exercise the XAML and its event handlers.</summary>
    public class AutoPlaylistWindowTests
    {
        private static void RunOnStaThread(Action action)
        {
            ExceptionDispatchInfo? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        private static MidiSong[] Library() => new[]
        {
            PlaylistBuilderTests.Song("easy", 10, 2, 95),
            PlaylistBuilderTests.Song("medium", 10, 6, 92),
            PlaylistBuilderTests.Song("hard", 10, 12, 91),
            PlaylistBuilderTests.Song("lowfit", 10, 1, 40),
            new MidiSong { Title = "pending" },
        };

        private static T Find<T>(AutoPlaylistWindow window, string name) where T : class =>
            Assert.IsType<T>(window.FindName(name));

        [Fact]
        public void ShowsThePreviewForTheSavedCriteria()
        {
            RunOnStaThread(() =>
            {
                var window = new AutoPlaylistWindow(Library(), new AutoPlaylistCriteria { TargetMinutes = 30, MinFitPercent = 90 }, "37 keys");

                Assert.Equal(new[] { "easy", "medium", "hard" }, window.Result.Select(s => s.Title));
                Assert.Equal(3, Find<ListView>(window, "PreviewListView").Items.Count);
                Assert.True(Find<Button>(window, "ApplyButton").IsEnabled);
                Assert.Contains("3 of 3 matching songs, 30:00 in total", Find<TextBlock>(window, "SummaryText").Text);
                Assert.Contains("1 not analyzed", Find<TextBlock>(window, "SummaryText").Text);
                Assert.Contains("37 keys", Find<TextBlock>(window, "ModeText").Text);
                window.Close();
            });
        }

        [Fact]
        public void EasyPresetFillsTheLimitAndTypingSwitchesToCustom()
        {
            RunOnStaThread(() =>
            {
                var window = new AutoPlaylistWindow(Library(), new AutoPlaylistCriteria { TargetMinutes = 0 }, "37 keys");
                var preset = Find<ComboBox>(window, "DifficultyPresetBox");
                var limit = Find<TextBox>(window, "MaxDifficultyBox");

                Assert.Equal(4, window.Result.Count);
                Assert.Equal(string.Empty, limit.Text);

                preset.SelectedIndex = 1; // Easy = easiest third of the 4 analyzed songs
                Assert.Equal("2", limit.Text);
                Assert.Equal(AutoPlaylistCriteria.PresetEasy, window.Criteria.DifficultyPreset);
                Assert.Equal(new[] { "lowfit", "easy" }, window.Result.Select(s => s.Title));

                limit.Text = "6.5";
                Assert.Equal("Custom", ((ComboBoxItem)preset.SelectedItem).Tag);
                Assert.Equal(6.5, window.Criteria.MaxNotesPerSecond);
                Assert.Equal(3, window.Result.Count);
                window.Close();
            });
        }

        [Fact]
        public void ImpossibleOrInvalidCriteriaShowAMessageInsteadOfFailing()
        {
            RunOnStaThread(() =>
            {
                var window = new AutoPlaylistWindow(Library(), new AutoPlaylistCriteria(), "15 keys");
                var minFit = Find<TextBox>(window, "MinFitBox");
                var empty = Find<TextBlock>(window, "EmptyText");
                var apply = Find<Button>(window, "ApplyButton");

                minFit.Text = "99";
                Assert.Empty(window.Result);
                Assert.False(apply.IsEnabled);
                Assert.Equal(System.Windows.Visibility.Visible, empty.Visibility);
                Assert.Contains("No songs match", empty.Text);

                minFit.Text = "abc";
                Assert.Empty(window.Result);
                Assert.False(apply.IsEnabled);
                Assert.Contains("Check the highlighted values", empty.Text);

                minFit.Text = "";
                Find<TextBox>(window, "TargetMinutesBox").Text = "5";
                Assert.Empty(window.Result);
                Assert.Contains("longer than the total duration", empty.Text);

                Find<TextBox>(window, "TargetMinutesBox").Text = "20";
                Assert.Equal(2, window.Result.Count);
                Assert.True(apply.IsEnabled);
                Assert.Equal(System.Windows.Visibility.Collapsed, empty.Visibility);
                window.Close();
            });
        }

        [Fact]
        public void RandomOrderOnlyReshufflesOnRequest()
        {
            RunOnStaThread(() =>
            {
                var songs = Enumerable.Range(0, 40).Select(i => PlaylistBuilderTests.Song($"s{i:00}", 1, i + 1, 100)).ToArray();
                var window = new AutoPlaylistWindow(songs, new AutoPlaylistCriteria { TargetMinutes = 0 }, "37 keys");
                var shuffle = Find<Button>(window, "ShuffleAgainButton");
                Assert.Equal(System.Windows.Visibility.Collapsed, shuffle.Visibility);

                Find<ComboBox>(window, "OrderBox").SelectedIndex = 1;
                Assert.Equal(System.Windows.Visibility.Visible, shuffle.Visibility);
                Assert.Equal(AutoPlaylistOrder.Random, window.Criteria.Order);
                string first = string.Join(",", window.Result.Select(s => s.Title));

                Find<TextBox>(window, "MinFitBox").Text = "50";
                Assert.Equal(first, string.Join(",", window.Result.Select(s => s.Title)));

                shuffle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.NotEqual(first, string.Join(",", window.Result.Select(s => s.Title)));
                Assert.Equal(40, window.Result.Count);
                window.Close();
            });
        }
    }
}
