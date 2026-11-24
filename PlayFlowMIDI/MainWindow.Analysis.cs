using System.Windows.Controls;
using System.Windows.Threading;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Background song analysis for the playlist (difficulty and fit columns).
    /// Lives in its own file so MainWindow.xaml.cs only needs one-line hooks.
    /// </summary>
    public partial class MainWindow
    {
        private const string SortByDuration = "Duration";
        private const string SortByDifficulty = "Difficulty";
        private const string SortByFit = "Fit %";

        private CancellationTokenSource? _analysisCts;
        private bool _analysisQueued;

        private readonly record struct AnalysisProgress(MidiSong Song, MidiAnalysisResult Result, int Done, int Total);

        private (string? Mode, bool AutoAdapt15Keys) GetFitContext()
        {
            var profile = _config.Profiles.FirstOrDefault(p => p.Name == _config.LastSelectedProfile);
            return (profile?.SelectedMode, _config.Settings.AutoAdapt15Keys);
        }

        private string CurrentSortBy => (SortByComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

        /// <summary>
        /// Schedules an analysis pass once the current UI work is done. Safe to call before the
        /// playlist is modified and any number of times: calls are merged, and songs whose file
        /// has not changed since their last analysis are skipped.
        /// </summary>
        private void QueueSongAnalysis()
        {
            if (_analysisQueued) return;
            _analysisQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _analysisQueued = false;
                _ = AnalyzeSongsAsync();
            }));
        }

        private async Task AnalyzeSongsAsync()
        {
            _analysisCts?.Cancel();
            var cts = new CancellationTokenSource();
            _analysisCts = cts;
            var token = cts.Token;

            var songs = _masterPlaylist.ToList();
            int shown = 0;
            var progress = new Progress<AnalysisProgress>(p =>
            {
                if (token.IsCancellationRequested) return;

                var (mode, autoAdapt) = GetFitContext();
                p.Song.ApplyAnalysis(p.Result, mode, autoAdapt);

                if (p.Done > shown)
                {
                    shown = p.Done;
                    SongCountText.Text = $"Analyzing songs {p.Done}/{p.Total}...";
                }
            });

            int analyzed;
            try
            {
                analyzed = await Task.Run(() => RunAnalysis(songs, progress, token), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                InputDebugLog($"AnalyzeSongsAsync error: {ex}");
                return;
            }
            finally
            {
                if (_analysisCts == cts) _analysisCts = null;
                cts.Dispose();
            }

            if (analyzed == 0) return;

            SongCountText.Text = $"{PlaylistListView.Items.Count} songs loaded";

            string sortBy = CurrentSortBy;
            if (sortBy == SortByDifficulty || sortBy == SortByFit || sortBy == SortByDuration)
                ResortPlaylistKeepingSelection();

            // SaveConfig also refreshes the game window target; leave that alone mid-song.
            // The results are in memory and get written by the next regular save.
            if (!_isPlaying) SaveConfig();
        }

        /// <summary>Runs on a worker thread. Returns how many songs were (re)analyzed.</summary>
        private static int RunAnalysis(List<MidiSong> songs, IProgress<AnalysisProgress> progress, CancellationToken token)
        {
            var pending = new List<MidiSong>();
            foreach (var song in songs)
            {
                token.ThrowIfCancellationRequested();
                var (size, modified) = MidiAnalyzer.GetFileStamp(song.FilePath);
                if (!song.Analysis.Matches(size, modified)) pending.Add(song);
            }

            if (pending.Count == 0) return 0;

            // Leave cores free: playback timing matters more than finishing the scan quickly.
            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            };

            int done = 0;
            Parallel.ForEach(pending, options, song =>
            {
                var result = MidiAnalyzer.Analyze(song.FilePath);
                progress.Report(new AnalysisProgress(song, result, Interlocked.Increment(ref done), pending.Count));
            });

            return pending.Count;
        }

        /// <summary>Shows the fit of the key mode in use. Cheap: nothing is read from disk.</summary>
        private void RefreshFitColumn()
        {
            var (mode, autoAdapt) = GetFitContext();

            bool changed = false;
            foreach (var song in _masterPlaylist)
            {
                changed |= song.UpdateFit(mode, autoAdapt);
            }

            if (changed && CurrentSortBy == SortByFit) ResortPlaylistKeepingSelection();
        }

        private void ResortPlaylistKeepingSelection()
        {
            var selected = PlaylistListView.SelectedItems.Cast<MidiSong>().ToList();
            SortPlaylist();
            foreach (var song in selected)
            {
                if (PlaylistListView.Items.Contains(song)) PlaylistListView.SelectedItems.Add(song);
            }
        }
    }
}
