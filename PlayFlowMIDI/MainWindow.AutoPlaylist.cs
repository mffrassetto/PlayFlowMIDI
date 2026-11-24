using System.Windows;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Auto Playlist hook-up. Applying one only changes what the list shows (and therefore what
    /// next/previous/shuffle play); no song is removed from the saved playlist.
    /// </summary>
    public partial class MainWindow
    {
        private const string AutoPlaylistButtonText = "Auto Playlist";
        private const string ShowAllSongsButtonText = "Show All Songs";

        /// <summary>Songs of the applied Auto Playlist in playing order, or null when the full playlist is shown.</summary>
        private List<MidiSong>? _autoPlaylist;

        private void AutoPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPlaylist != null)
            {
                SetAutoPlaylist(null);
                ResortPlaylistKeepingSelection();
                return;
            }

            var (mode, autoAdapt) = GetFitContext();
            string modeLabel = string.IsNullOrEmpty(mode) ? "default range"
                : mode == KeyModeRanges.Mode15 && autoAdapt ? "15 keys, auto-adapt"
                : $"{mode} keys";

            var window = new AutoPlaylistWindow(_masterPlaylist, _config.Main.AutoPlaylist, modeLabel,
                name => ConfigMigrations.ValidatePlaylistName(_config, name))
            {
                Owner = this,
                Topmost = Topmost,
            };
            if (window.ShowDialog() != true) return;

            _config.Main.AutoPlaylist = window.Criteria;

            if (window.SaveAsPlaylistName != null)
            {
                AddPlaylistFromSongs(window.SaveAsPlaylistName, window.Result);
                return;
            }

            SetAutoPlaylist(window.Result);
            SortPlaylist();
            SaveConfig();
        }

        private void SetAutoPlaylist(List<MidiSong>? songs)
        {
            _autoPlaylist = songs;
            AutoPlaylistButton.Content = songs == null ? AutoPlaylistButtonText : ShowAllSongsButtonText;
            AutoPlaylistButton.ToolTip = songs == null
                ? "Build a playlist from the analyzed songs by duration, difficulty and fit"
                : "An Auto Playlist is applied (Sort By is ignored). Click to show every song again.";
        }

        /// <summary>
        /// Called by SortPlaylist with the songs it is about to show (already filtered by the
        /// search box). With an Auto Playlist applied, only its songs are kept, in its order.
        /// </summary>
        private List<MidiSong> ApplyAutoPlaylistView(List<MidiSong> songs)
        {
            if (_autoPlaylist == null) return songs;

            // Songs removed from the playlist (or a newly loaded folder) drop out of the view.
            var master = _masterPlaylist.ToHashSet();
            _autoPlaylist.RemoveAll(s => !master.Contains(s));
            if (_autoPlaylist.Count == 0)
            {
                SetAutoPlaylist(null);
                return songs;
            }

            var visible = songs.ToHashSet();
            return _autoPlaylist.Where(visible.Contains).ToList();
        }
    }
}
