using System.Text.Json;

namespace PlayFlowMIDI.Tests
{
    public class ConfigCompatibilityTests
    {
        // Shape written by PlayFlowMIDI 1.2.2.0: playlist entries have no "Analysis".
        internal const string OldConfigJson = """
        {
          "ConfigVersion": 1,
          "Main": {
            "FolderPath": "C:\\Midis",
            "SortByIndex": 3,
            "SortAscending": false,
            "PlaybackMode": "RepeatPlaylist",
            "Shuffle": true,
            "LastPlayedSongPath": "C:\\Midis\\a.mid",
            "Playlist": [
              {
                "Title": "a",
                "FilePath": "C:\\Midis\\a.mid",
                "BPM": "120",
                "TimeAdded": "2026-01-02 03:04:05",
                "Duration": "3:05",
                "CustomOrder": 0,
                "DisabledTrackIndices": [2],
                "TrackStatesManuallySet": true
              }
            ]
          },
          "Settings": { "OctaveHandling": "Wrap", "AutoAdapt15Keys": true },
          "Profiles": [ { "Name": "Heartopia", "SelectedMode": "15", "KeyMappings": {} } ],
          "LastSelectedProfile": "Heartopia"
        }
        """;

        [Fact]
        public void OldConfigLoadsWithUnanalyzedSongs()
        {
            var config = JsonSerializer.Deserialize<AppConfig>(OldConfigJson)!;

            var song = Assert.Single(config.Main.Playlist!);
            Assert.Equal("a", song.Title);
            Assert.Equal("120", song.BPM);
            Assert.Equal("3:05", song.Duration);
            Assert.Equal(new[] { 2 }, song.DisabledTrackIndices);
            Assert.True(song.TrackStatesManuallySet);
            Assert.Equal(3, config.Main.SortByIndex);
            Assert.Equal(1, config.ConfigVersion);

            Assert.NotNull(song.Analysis);
            Assert.Equal(0, song.Analysis.Version);
            Assert.False(song.Analysis.IsCurrent);
        }

        [Fact]
        public void AnalysisSurvivesASaveAndLoad()
        {
            var config = JsonSerializer.Deserialize<AppConfig>(OldConfigJson)!;
            config.Main.Playlist![0].Analysis = new MidiAnalysisData
            {
                Version = MidiAnalyzer.CurrentVersion,
                FileSize = 1234,
                FileModifiedUtcTicks = 638000000000000000,
                DurationSeconds = 185.5,
                NoteCount = 742,
                NotesPerSecond = 4.0,
                TrackCount = 3,
                MinNote = 43,
                MaxNote = 88,
                Fit37 = 91.5,
                Fit36 = 90.2,
                Fit15 = 40,
                Fit15Adapted = 77.7,
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var loaded = JsonSerializer.Deserialize<AppConfig>(json)!.Main.Playlist![0].Analysis;

            Assert.DoesNotContain("IsCurrent", json);
            Assert.DoesNotContain("HasResult", json);
            Assert.True(loaded.Matches(1234, 638000000000000000));
            Assert.Equal(4.0, loaded.NotesPerSecond);
            Assert.Equal(3, loaded.TrackCount);
            Assert.Equal(43, loaded.MinNote);
            Assert.Equal(88, loaded.MaxNote);
            Assert.Equal(91.5, loaded.GetFit("37", false));
            Assert.Equal(77.7, loaded.GetFit("15", true));
        }

        [Fact]
        public void SongRowShowsPendingThenResultThenUnavailable()
        {
            var song = new MidiSong();
            Assert.Equal("…", song.DifficultyText);
            Assert.Equal("…", song.FitText);

            var changed = new List<string?>();
            song.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            song.ApplyAnalysis(new MidiAnalysisResult
            {
                BPM = "90",
                Duration = "1:30",
                Data = new MidiAnalysisData
                {
                    Version = MidiAnalyzer.CurrentVersion,
                    NoteCount = 10,
                    NotesPerSecond = 3.26,
                    Fit37 = 100,
                    Fit15 = 42.4,
                },
            }, "15", false);

            Assert.Equal("90", song.BPM);
            Assert.Equal("1:30", song.Duration);
            Assert.Equal(3.26.ToString("0.0"), song.DifficultyText);
            Assert.Equal("42%", song.FitText);
            Assert.Contains(string.Empty, changed);

            Assert.True(song.UpdateFit("37", false));
            Assert.Equal("100%", song.FitText);
            Assert.False(song.UpdateFit("37", false));

            song.ApplyAnalysis(new MidiAnalysisResult
            {
                Data = new MidiAnalysisData { Version = MidiAnalyzer.CurrentVersion, Failed = true },
            }, "37", false);

            Assert.Equal("–", song.DifficultyText);
            Assert.Equal("–", song.FitText);
            Assert.Equal("90", song.BPM);
            Assert.Equal(0, song.NotesPerSecond);
        }
    }
}
