using System.Windows;
using System.Windows.Controls;

namespace PlayFlowMIDI
{
    /// <summary>
    /// Named playlists. _masterPlaylist always holds the songs of the selected playlist; the
    /// others stay in _config.Main.Playlists until they are selected.
    /// </summary>
    public partial class MainWindow
    {
        private const string SortByCustomOrder = "Custom Order";

        private bool _updatingPlaylistSelector;

        private PlaylistConfig ActivePlaylistConfig
        {
            get
            {
                var active = ConfigMigrations.FindPlaylist(_config, _config.Main.SelectedPlaylist);
                if (active == null)
                {
                    ConfigMigrations.MigrateToNamedPlaylists(_config);
                    active = ConfigMigrations.FindPlaylist(_config, _config.Main.SelectedPlaylist)!;
                }
                return active;
            }
        }

        /// <summary>Called by ApplyConfig: upgrades older configs, fills the selector and loads the selected playlist.</summary>
        private void InitializePlaylists()
        {
            if (ConfigMigrations.MigrateToNamedPlaylists(_config))
                ConfigMigrations.BackupConfigFile(AppConfig.GetConfigPath());

            RefreshPlaylistSelector();
            LoadActivePlaylist();
        }

        /// <summary>Replaces _masterPlaylist with the songs of the selected playlist.</summary>
        private void LoadActivePlaylist()
        {
            var active = ActivePlaylistConfig;

            _masterPlaylist.Clear();
            foreach (var songConfig in active.Songs)
            {
                _masterPlaylist.Add(new MidiSong
                {
                    Title = songConfig.Title,
                    FilePath = songConfig.FilePath,
                    BPM = songConfig.BPM,
                    TimeAdded = songConfig.TimeAdded,
                    Duration = songConfig.Duration,
                    CustomOrder = songConfig.CustomOrder,
                    DisabledTrackIndices = songConfig.DisabledTrackIndices ?? new(),
                    TrackStatesManuallySet = songConfig.TrackStatesManuallySet,
                    Analysis = songConfig.Analysis ?? new()
                });
            }

            if (FolderPathTextBox.Text != active.FolderPath) FolderPathTextBox.Text = active.FolderPath;
        }

        /// <summary>Called by SaveConfig: writes _masterPlaylist back into the selected playlist.</summary>
        private void StoreActivePlaylist()
        {
            var active = ActivePlaylistConfig;
            active.FolderPath = FolderPathTextBox.Text;
            active.Songs = _masterPlaylist.Select(ToSongConfig).ToList();
        }

        private static MidiSongConfig ToSongConfig(MidiSong song) => new()
        {
            Title = song.Title,
            FilePath = song.FilePath,
            BPM = song.BPM,
            TimeAdded = song.TimeAdded,
            Duration = song.Duration,
            CustomOrder = song.CustomOrder,
            DisabledTrackIndices = new List<int>(song.DisabledTrackIndices),
            TrackStatesManuallySet = song.TrackStatesManuallySet,
            Analysis = song.Analysis
        };

        private void RefreshPlaylistSelector()
        {
            _updatingPlaylistSelector = true;
            PlaylistComboBox.Items.Clear();
            foreach (var playlist in _config.Main.Playlists)
            {
                PlaylistComboBox.Items.Add(playlist.Name);
            }
            PlaylistComboBox.SelectedItem = ActivePlaylistConfig.Name;
            _updatingPlaylistSelector = false;
        }

        /// <summary>
        /// Shows another playlist. A song that is playing keeps playing; next/previous then move
        /// inside the new playlist.
        /// </summary>
        /// <param name="storeCurrent">False when the playlist being left no longer exists (it was deleted).</param>
        private void SwitchToPlaylist(string name, bool storeCurrent = true)
        {
            if (storeCurrent) SaveConfig();

            bool wasLoading = _isLoading;
            _isLoading = true;
            _config.Main.SelectedPlaylist = name;
            SetAutoPlaylist(null);
            LoadActivePlaylist();
            RefreshPlaylistSelector();
            _isLoading = wasLoading;

            // The Tracks panel edits a song object; do not leave it pointing at one that is no longer saved.
            if (_tracksSong != null && !_masterPlaylist.Contains(_tracksSong)) ShowTracksForSong(null);

            // Same as on start-up: picks up new files from the playlist's folder, then sorts and analyzes.
            ReloadSongs();
            SaveConfig();
        }

        private void PlaylistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingPlaylistSelector || _isLoading) return;
            if (PlaylistComboBox.SelectedItem is not string name) return;
            if (string.Equals(name, ActivePlaylistConfig.Name, StringComparison.OrdinalIgnoreCase)) return;

            SwitchToPlaylist(name);
        }

        private string? AskPlaylistName(string title, string initialValue, PlaylistConfig? renaming = null, Window? owner = null)
        {
            var prompt = new TextPromptWindow(title, "Playlist name", initialValue,
                value => ConfigMigrations.ValidatePlaylistName(_config, value, renaming))
            {
                Owner = owner ?? this,
                Topmost = Topmost,
            };
            return prompt.ShowDialog() == true ? prompt.Value : null;
        }

        private void NewPlaylist_Click(object sender, RoutedEventArgs e)
        {
            string? name = AskPlaylistName("New Playlist", string.Empty);
            if (name == null) return;

            AddPlaylist(name, new List<MidiSongConfig>());
        }

        /// <summary>Adds a playlist that is not tied to a folder and selects it.</summary>
        private void AddPlaylist(string name, List<MidiSongConfig> songs)
        {
            SaveConfig();
            _config.Main.Playlists.Add(new PlaylistConfig { Name = name, Songs = songs });
            SwitchToPlaylist(name, storeCurrent: false);
        }

        /// <summary>
        /// Saves an Auto Playlist result as its own playlist. The songs are copies, so track
        /// choices made later in one playlist do not change the other.
        /// </summary>
        private void AddPlaylistFromSongs(string name, List<MidiSong> songs)
        {
            var copies = songs.Select(ToSongConfig).ToList();
            for (int i = 0; i < copies.Count; i++) copies[i].CustomOrder = i;

            AddPlaylist(name, copies);

            // The generated order is the playlist's custom order; show it that way.
            SortDirectionButton.Content = "▲";
            SortDirectionButton.ToolTip = "Ascending order";
            foreach (ComboBoxItem item in SortByComboBox.Items)
            {
                if (item.Content?.ToString() == SortByCustomOrder)
                {
                    SortByComboBox.SelectedItem = item;
                    break;
                }
            }
            SortPlaylist();
            SaveConfig();
        }

        private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var active = ActivePlaylistConfig;
            string? name = AskPlaylistName("Rename Playlist", active.Name, renaming: active);
            if (name == null || name == active.Name) return;

            active.Name = name;
            _config.Main.SelectedPlaylist = name;
            RefreshPlaylistSelector();
            SaveConfig();
        }

        private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var playlists = _config.Main.Playlists;
            var active = ActivePlaylistConfig;

            if (playlists.Count <= 1)
            {
                System.Windows.MessageBox.Show(this, "This is the only playlist. Create another one before deleting it.",
                    "Delete Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = System.Windows.MessageBox.Show(this,
                $"Delete the playlist \"{active.Name}\" ({_masterPlaylist.Count} songs)?\n\nThe MIDI files themselves are not deleted.",
                "Delete Playlist", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            int index = playlists.IndexOf(active);
            playlists.Remove(active);
            string next = playlists[Math.Min(index, playlists.Count - 1)].Name;
            SwitchToPlaylist(next, storeCurrent: false);
        }
    }
}
