namespace PlayFlowMIDI.Tests
{
    public class PlaylistBuilderTests
    {
        private static MidiSong Song(string title, double minutes, double notesPerSecond, double fit, int tracks = 1)
        {
            var song = new MidiSong { Title = title };
            song.ApplyAnalysis(new MidiAnalysisResult
            {
                Data = new MidiAnalysisData
                {
                    Version = MidiAnalyzer.CurrentVersion,
                    DurationSeconds = minutes * 60,
                    NoteCount = 100,
                    NotesPerSecond = notesPerSecond,
                    TrackCount = tracks,
                    Fit37 = fit,
                },
            }, KeyModeRanges.Mode37, false);
            return song;
        }

        private static readonly AutoPlaylistCriteria NoLimits = new() { TargetMinutes = 0 };

        private static string Titles(AutoPlaylistResult result) => string.Join(",", result.Songs.Select(s => s.Title));

        [Fact]
        public void AppliesDifficultyFitAndTrackFilters()
        {
            var songs = new[]
            {
                Song("easy", 3, 2, 95, tracks: 1),
                Song("hard", 3, 12, 95, tracks: 1),
                Song("lowfit", 3, 2, 60, tracks: 1),
                Song("manytracks", 3, 2, 95, tracks: 9),
                Song("edge", 3, 5, 90, tracks: 4),
            };

            var result = PlaylistBuilder.Build(songs, new AutoPlaylistCriteria
            {
                TargetMinutes = 0,
                MaxNotesPerSecond = 5,
                MinFitPercent = 90,
                MaxTracks = 4,
            });

            Assert.Equal("easy,edge", Titles(result));
            Assert.Equal(2, result.MatchingCount);
            Assert.Equal(360, result.TotalSeconds);
        }

        [Fact]
        public void DifficultyOrderGoesFromEasyToHard()
        {
            var songs = new[] { Song("c", 1, 9, 100), Song("a", 1, 1, 100), Song("b2", 1, 4, 100), Song("b1", 1, 4, 100) };

            Assert.Equal("a,b1,b2,c", Titles(PlaylistBuilder.Build(songs, NoLimits)));
        }

        [Fact]
        public void NeverGoesOverTheTargetAndFillsTheGapWithShorterSongs()
        {
            var songs = new[]
            {
                Song("a", 12, 1, 100),
                Song("b", 12, 2, 100),
                Song("c", 12, 3, 100),
                Song("d", 5, 4, 100),
                Song("e", 2, 5, 100),
            };

            var result = PlaylistBuilder.Build(songs, new AutoPlaylistCriteria { TargetMinutes = 30 });

            // a + b = 24 min; c would pass 30, d fits (29), e would pass.
            Assert.Equal("a,b,d", Titles(result));
            Assert.Equal(29 * 60, result.TotalSeconds);
            Assert.Equal(5, result.MatchingCount);
        }

        [Fact]
        public void NothingMatchingReturnsAnEmptyResult()
        {
            var songs = new[] { Song("a", 3, 8, 50) };

            var result = PlaylistBuilder.Build(songs, new AutoPlaylistCriteria { MinFitPercent = 90 });

            Assert.Empty(result.Songs);
            Assert.Equal(0, result.MatchingCount);
            Assert.Equal(0, result.TotalSeconds);
        }

        [Fact]
        public void EmptyLibraryAndTooShortTargetDoNotThrow()
        {
            Assert.Empty(PlaylistBuilder.Build(Array.Empty<MidiSong>(), new AutoPlaylistCriteria()).Songs);

            var result = PlaylistBuilder.Build(new[] { Song("long", 45, 1, 100) }, new AutoPlaylistCriteria { TargetMinutes = 30 });
            Assert.Empty(result.Songs);
            Assert.Equal(1, result.MatchingCount);
        }

        [Fact]
        public void SongsWithoutAnalysisAreLeftOutAndCounted()
        {
            var failed = new MidiSong { Title = "failed" };
            failed.ApplyAnalysis(new MidiAnalysisResult
            {
                Data = new MidiAnalysisData { Version = MidiAnalyzer.CurrentVersion, Failed = true },
            }, KeyModeRanges.Mode37, false);

            var songs = new[] { Song("ok", 3, 2, 100), new MidiSong { Title = "pending" }, failed };

            var result = PlaylistBuilder.Build(songs, NoLimits);

            Assert.Equal("ok", Titles(result));
            Assert.Equal(2, result.NotAnalyzedCount);
        }

        [Fact]
        public void RandomOrderIsRepeatableWithASeedAndKeepsTheSameSongs()
        {
            var songs = Enumerable.Range(0, 20).Select(i => Song($"s{i:00}", 1, i, 100)).ToArray();
            var criteria = new AutoPlaylistCriteria { TargetMinutes = 0, Order = AutoPlaylistOrder.Random };

            string first = Titles(PlaylistBuilder.Build(songs, criteria, new Random(7)));
            string again = Titles(PlaylistBuilder.Build(songs, criteria, new Random(7)));
            string other = Titles(PlaylistBuilder.Build(songs, criteria, new Random(8)));
            string sorted = Titles(PlaylistBuilder.Build(songs, NoLimits));

            Assert.Equal(first, again);
            Assert.NotEqual(first, other);
            Assert.NotEqual(first, sorted);
            Assert.Equal(sorted.Split(',').Order(), first.Split(',').Order());
        }

        [Fact]
        public void RandomOrderStillRespectsTheTarget()
        {
            var songs = Enumerable.Range(0, 30).Select(i => Song($"s{i}", 4, i, 100)).ToArray();
            var criteria = new AutoPlaylistCriteria { TargetMinutes = 30, Order = AutoPlaylistOrder.Random };

            var result = PlaylistBuilder.Build(songs, criteria, new Random(1));

            Assert.Equal(7, result.Songs.Count);
            Assert.Equal(28 * 60, result.TotalSeconds);
        }

        [Fact]
        public void DifficultyAtFractionSplitsTheLibrary()
        {
            var songs = new[] { Song("a", 1, 2, 100), Song("b", 1, 4, 100), Song("c", 1, 6, 100), new MidiSong() };

            Assert.Equal(2, PlaylistBuilder.DifficultyAtFraction(songs, 1.0 / 3));
            Assert.Equal(4, PlaylistBuilder.DifficultyAtFraction(songs, 2.0 / 3));
            Assert.Equal(6, PlaylistBuilder.DifficultyAtFraction(songs, 1));
            Assert.Equal(0, PlaylistBuilder.DifficultyAtFraction(Array.Empty<MidiSong>(), 0.5));
        }

        [Fact]
        public void CriteriaSurviveASaveAndOldConfigsGetDefaults()
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("""{ "Main": { "FolderPath": "x" } }""")!;
            Assert.Equal(30, config.Main.AutoPlaylist.TargetMinutes);
            Assert.Equal(AutoPlaylistOrder.DifficultyAscending, config.Main.AutoPlaylist.Order);

            config.Main.AutoPlaylist = new AutoPlaylistCriteria { TargetMinutes = 45, MinFitPercent = 90, Order = AutoPlaylistOrder.Random };
            string json = System.Text.Json.JsonSerializer.Serialize(config);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!.Main.AutoPlaylist;

            Assert.Contains("\"Order\":\"Random\"", json);
            Assert.Equal(45, loaded.TargetMinutes);
            Assert.Equal(90, loaded.MinFitPercent);
            Assert.Equal(AutoPlaylistOrder.Random, loaded.Order);
        }
    }
}
