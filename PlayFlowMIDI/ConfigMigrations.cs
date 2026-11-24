using System.IO;

namespace PlayFlowMIDI
{
    /// <summary>config.json upgrades that do not depend on the UI, so they can be unit tested.</summary>
    public static class ConfigMigrations
    {
        /// <summary>ConfigVersion from which the playlist is a named collection (Main.Playlists).</summary>
        public const int NamedPlaylistsVersion = 2;
        public const string DefaultPlaylistName = "Default";
        public const string BackupFileName = "config.before-named-playlists.json";

        /// <summary>
        /// Moves the single playlist of older configs into a playlist called "Default" and makes
        /// sure there is always a valid selected playlist. Safe to call on every start.
        /// Run it after the ConfigVersion 1 migration: it raises ConfigVersion to 2.
        /// Returns true when an old playlist was converted.
        /// </summary>
        public static bool MigrateToNamedPlaylists(AppConfig config)
        {
            var main = config.Main;
            main.Playlists ??= new();
            main.Playlists.RemoveAll(p => p == null);

            // Decided by content, not only by version: a config saved by an older build after a
            // downgrade keeps the new version number but carries its songs in the old place.
            bool converted = false;
            if (main.Playlists.Count == 0)
            {
                converted = main.Playlist != null;
                main.Playlists.Add(new PlaylistConfig
                {
                    Name = DefaultPlaylistName,
                    FolderPath = main.FolderPath ?? string.Empty,
                    Songs = main.Playlist ?? new(),
                });
                main.SelectedPlaylist = DefaultPlaylistName;
            }
            main.Playlist = null;

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var playlist in main.Playlists)
            {
                playlist.Songs ??= new();
                playlist.FolderPath ??= string.Empty;
                playlist.Name = UniqueName(playlist.Name, usedNames);
                usedNames.Add(playlist.Name);
            }

            if (FindPlaylist(config, main.SelectedPlaylist) == null)
                main.SelectedPlaylist = main.Playlists[0].Name;

            if (config.ConfigVersion < NamedPlaylistsVersion) config.ConfigVersion = NamedPlaylistsVersion;
            return converted;
        }

        public static PlaylistConfig? FindPlaylist(AppConfig config, string? name) =>
            config.Main.Playlists.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Null when the name can be used; otherwise the message to show the user.</summary>
        public static string? ValidatePlaylistName(AppConfig config, string? name, PlaylistConfig? renaming = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Type a name for the playlist.";

            var existing = FindPlaylist(config, name.Trim());
            if (existing != null && existing != renaming) return "There is already a playlist with this name.";
            return null;
        }

        /// <summary>Keeps a copy of the file as it was before the conversion. Never overwrites an existing copy.</summary>
        public static void BackupConfigFile(string configPath)
        {
            try
            {
                string backupPath = Path.Combine(Path.GetDirectoryName(configPath) ?? string.Empty, BackupFileName);
                if (File.Exists(configPath) && !File.Exists(backupPath)) File.Copy(configPath, backupPath);
            }
            catch { }
        }

        private static string UniqueName(string? name, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? DefaultPlaylistName : name.Trim();
            string candidate = baseName;
            for (int i = 2; usedNames.Contains(candidate); i++)
            {
                candidate = $"{baseName} ({i})";
            }
            return candidate;
        }
    }
}
