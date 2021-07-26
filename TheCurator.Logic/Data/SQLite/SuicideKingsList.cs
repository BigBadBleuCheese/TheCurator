using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsList
    {
        [Indexed(Name = "UX_SuicideKingsList", Order = 1, Unique = true), NotNull]
        public long ChannelId { get; set; }

        [PrimaryKey, AutoIncrement]
        public int ListId { get; set; }

        [Indexed(Name = "UX_SuicideKingsList", Order = 2, Unique = true), NotNull]
        public string? Name { get; set; }
    }
}
