using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsListEntry
    {
        [Indexed(Name = "UX_SuicideKingsListEntry", Order = 1, Unique = true), NotNull]
        public int ListId { get; set; }

        [Indexed(Name = "UX_SuicideKingsListEntry", Order = 2, Unique = true), NotNull]
        public int MemberId { get; set; }

        [NotNull]
        public int Position { get; set; }
    }
}
