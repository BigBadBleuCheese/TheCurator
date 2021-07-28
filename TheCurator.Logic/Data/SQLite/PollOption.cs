using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class PollOption
    {
        [NotNull]
        public string? EmoteName { get; set; }

        [NotNull]
        public string? Name { get; set; }

        [PrimaryKey, AutoIncrement]
        public int OptionId { get; set; }

        [NotNull]
        public int Order { get; set; }

        [Indexed, NotNull]
        public int PollId { get; set; }
    }
}
