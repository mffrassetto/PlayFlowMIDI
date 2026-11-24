using System.Text.Json;

namespace PlayFlowMIDI.Tests
{
    public class ConfigMigrationsTests
    {
        private static AppConfig LoadOld() => JsonSerializer.Deserialize<AppConfig>(ConfigCompatibilityTests.OldConfigJson)!;

        private static string Save(AppConfig config) =>
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

        [Fact]
        public void OldPlaylistBecomesDefaultWithoutLosingAnything()
        {
            var config = LoadOld();
            string songBefore = JsonSerializer.Serialize(config.Main.Playlist![0]);

            bool converted = ConfigMigrations.MigrateToNamedPlaylists(config);

            Assert.True(converted);
            Assert.Equal(2, config.ConfigVersion);
            Assert.Null(config.Main.Playlist);
            Assert.Equal("Default", config.Main.SelectedPlaylist);

            var playlist = Assert.Single(config.Main.Playlists);
            Assert.Equal("Default", playlist.Name);
            Assert.Equal("C:\\Midis", playlist.FolderPath);
            Assert.Equal(songBefore, JsonSerializer.Serialize(Assert.Single(playlist.Songs)));

            // Everything outside the playlist is untouched.
            Assert.Equal("C:\\Midis", config.Main.FolderPath);
            Assert.Equal(3, config.Main.SortByIndex);
            Assert.Equal("RepeatPlaylist", config.Main.PlaybackMode);
            Assert.True(config.Main.Shuffle);
            Assert.Equal("C:\\Midis\\a.mid", config.Main.LastPlayedSongPath);
            Assert.Equal("Heartopia", config.LastSelectedProfile);
            Assert.True(config.Settings.AutoAdapt15Keys);
        }

        [Fact]
        public void MigratedConfigIsSavedInTheNewShapeAndLoadsBackTheSame()
        {
            var config = LoadOld();
            ConfigMigrations.MigrateToNamedPlaylists(config);

            string json = Save(config);
            var reloaded = JsonSerializer.Deserialize<AppConfig>(json)!;
            bool convertedAgain = ConfigMigrations.MigrateToNamedPlaylists(reloaded);

            Assert.DoesNotContain("\"Playlist\":", json);
            Assert.Contains("\"Playlists\":", json);
            Assert.False(convertedAgain);
            Assert.Equal(json, Save(reloaded));
        }

        [Fact]
        public void MigrationIsIdempotentAndKeepsExistingPlaylists()
        {
            var config = LoadOld();
            ConfigMigrations.MigrateToNamedPlaylists(config);
            config.Main.Playlists.Add(new PlaylistConfig { Name = "Easy 30", Songs = { new MidiSongConfig { Title = "x" } } });
            config.Main.SelectedPlaylist = "Easy 30";
            string before = Save(config);

            Assert.False(ConfigMigrations.MigrateToNamedPlaylists(config));
            Assert.Equal(before, Save(config));
        }

        [Fact]
        public void BrandNewConfigGetsAnEmptyDefaultPlaylist()
        {
            var config = new AppConfig { ConfigVersion = 1 };

            Assert.False(ConfigMigrations.MigrateToNamedPlaylists(config));

            var playlist = Assert.Single(config.Main.Playlists);
            Assert.Equal("Default", playlist.Name);
            Assert.Empty(playlist.Songs);
            Assert.Equal("Default", config.Main.SelectedPlaylist);
            Assert.Equal(2, config.ConfigVersion);
        }

        [Fact]
        public void ConfigWrittenByAnOlderBuildAfterADowngradeIsConvertedAgain()
        {
            // The older build keeps ConfigVersion 2, drops "Playlists" and writes "Playlist".
            var config = JsonSerializer.Deserialize<AppConfig>(
                """{ "ConfigVersion": 2, "Main": { "FolderPath": "D:\\M", "Playlist": [ { "Title": "kept" } ] } }""")!;

            Assert.True(ConfigMigrations.MigrateToNamedPlaylists(config));
            Assert.Equal("kept", Assert.Single(Assert.Single(config.Main.Playlists).Songs).Title);
        }

        [Fact]
        public void BrokenNamesAndSelectionAreRepaired()
        {
            var config = JsonSerializer.Deserialize<AppConfig>("""
                {
                  "ConfigVersion": 2,
                  "Main": {
                    "SelectedPlaylist": "gone",
                    "Playlists": [
                      { "Name": "Mix", "Songs": [] },
                      { "Name": "mix", "Songs": null },
                      { "Name": "  ", "FolderPath": null },
                      null
                    ]
                  }
                }
                """)!;

            ConfigMigrations.MigrateToNamedPlaylists(config);

            Assert.Equal(new[] { "Mix", "mix (2)", "Default" }, config.Main.Playlists.Select(p => p.Name));
            Assert.All(config.Main.Playlists, p => Assert.NotNull(p.Songs));
            Assert.All(config.Main.Playlists, p => Assert.NotNull(p.FolderPath));
            Assert.Equal("Mix", config.Main.SelectedPlaylist);
        }

        [Fact]
        public void PlaylistNamesMustBeFilledAndUnique()
        {
            var config = LoadOld();
            ConfigMigrations.MigrateToNamedPlaylists(config);
            var existing = config.Main.Playlists[0];

            Assert.NotNull(ConfigMigrations.ValidatePlaylistName(config, ""));
            Assert.NotNull(ConfigMigrations.ValidatePlaylistName(config, "   "));
            Assert.NotNull(ConfigMigrations.ValidatePlaylistName(config, "default"));
            Assert.NotNull(ConfigMigrations.ValidatePlaylistName(config, " Default "));
            Assert.Null(ConfigMigrations.ValidatePlaylistName(config, "Easy 30"));
            // Renaming a playlist to its own name (or only changing its case) is allowed.
            Assert.Null(ConfigMigrations.ValidatePlaylistName(config, "DEFAULT", renaming: existing));
        }

        [Fact]
        public void BackupIsWrittenOnceAndNeverOverwritten()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PlayFlowMIDI.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string configPath = Path.Combine(folder, "config.json");
                string backupPath = Path.Combine(folder, ConfigMigrations.BackupFileName);

                ConfigMigrations.BackupConfigFile(configPath); // nothing to copy yet
                Assert.False(File.Exists(backupPath));

                File.WriteAllText(configPath, "original");
                ConfigMigrations.BackupConfigFile(configPath);
                Assert.Equal("original", File.ReadAllText(backupPath));

                File.WriteAllText(configPath, "changed later");
                ConfigMigrations.BackupConfigFile(configPath);
                Assert.Equal("original", File.ReadAllText(backupPath));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
