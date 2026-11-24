using System.Text.Json.Serialization;

namespace PlayFlowMIDI
{
    public enum AutoPlaylistOrder
    {
        /// <summary>Easiest songs are picked first and the result goes from easy to hard.</summary>
        DifficultyAscending,
        /// <summary>Songs are picked and played in random order.</summary>
        Random,
    }

    /// <summary>Auto Playlist criteria. Saved in config.json so the window reopens with the last values.</summary>
    public class AutoPlaylistCriteria
    {
        public const string PresetAny = "Any";
        public const string PresetEasy = "Easy";
        public const string PresetMedium = "Medium";
        public const string PresetCustom = "Custom";

        /// <summary>Total length to aim for; the result never goes over it. 0 = no limit.</summary>
        public double TargetMinutes { get; set; } = 30;
        /// <summary>Highest notes per second allowed. 0 = any.</summary>
        public double MaxNotesPerSecond { get; set; } = 0;
        /// <summary>Only used by the window to remember which difficulty option was chosen.</summary>
        public string DifficultyPreset { get; set; } = PresetAny;
        /// <summary>Lowest fit (0-100) for the key mode in use. 0 = any.</summary>
        public double MinFitPercent { get; set; } = 0;
        /// <summary>Highest number of tracks allowed. 0 = any.</summary>
        public int MaxTracks { get; set; } = 0;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AutoPlaylistOrder Order { get; set; } = AutoPlaylistOrder.DifficultyAscending;
    }

    public sealed class AutoPlaylistResult
    {
        public List<MidiSong> Songs { get; init; } = new();
        public double TotalSeconds { get; init; }
        /// <summary>Songs that pass the filters, before the duration limit is applied.</summary>
        public int MatchingCount { get; init; }
        /// <summary>Songs left out because they have no usable analysis yet (pending, unreadable or without notes).</summary>
        public int NotAnalyzedCount { get; init; }
    }

    /// <summary>Picks songs for an Auto Playlist. Pure logic: no UI and no file access.</summary>
    public static class PlaylistBuilder
    {
        public static AutoPlaylistResult Build(IEnumerable<MidiSong> songs, AutoPlaylistCriteria criteria, Random? random = null)
        {
            var all = songs.ToList();
            var usable = all.Where(IsUsable).ToList();

            var matching = usable.Where(s =>
                (criteria.MaxNotesPerSecond <= 0 || s.NotesPerSecond <= criteria.MaxNotesPerSecond) &&
                s.FitPercent >= criteria.MinFitPercent &&
                (criteria.MaxTracks <= 0 || s.Analysis.TrackCount <= criteria.MaxTracks)).ToList();

            if (criteria.Order == AutoPlaylistOrder.Random)
            {
                Shuffle(matching, random ?? Random.Shared);
            }
            else
            {
                matching = matching
                    .OrderBy(s => s.NotesPerSecond)
                    .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Walk the candidates in order and keep every song that still fits in the time left,
            // so the total gets as close to the target as possible without passing it.
            double targetSeconds = criteria.TargetMinutes * 60;
            var picked = new List<MidiSong>();
            double total = 0;
            foreach (var song in matching)
            {
                double length = song.Analysis.DurationSeconds;
                if (targetSeconds > 0 && total + length > targetSeconds) continue;

                picked.Add(song);
                total += length;
            }

            return new AutoPlaylistResult
            {
                Songs = picked,
                TotalSeconds = total,
                MatchingCount = matching.Count,
                NotAnalyzedCount = all.Count - usable.Count,
            };
        }

        /// <summary>
        /// Difficulty below which the given share (0-1) of the analyzed songs falls. Lets "easy"
        /// mean "easy for this library" instead of a fixed number. 0 when nothing is analyzed.
        /// </summary>
        public static double DifficultyAtFraction(IEnumerable<MidiSong> songs, double fraction)
        {
            var values = songs.Where(IsUsable).Select(s => s.NotesPerSecond).OrderBy(v => v).ToList();
            if (values.Count == 0) return 0;

            int index = (int)Math.Ceiling(fraction * values.Count) - 1;
            return values[Math.Clamp(index, 0, values.Count - 1)];
        }

        private static bool IsUsable(MidiSong song) =>
            song.Analysis.HasResult && song.Analysis.NoteCount > 0 && song.Analysis.DurationSeconds > 0;

        private static void Shuffle(List<MidiSong> list, Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
