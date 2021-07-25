using SQLite;
using System;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsMember
    {
        [Indexed(Name = "UX_SuicideKingsMember", Order = 1, Unique = true), NotNull]
        public int ChannelId { get; set; }

        [PrimaryKey, AutoIncrement]
        public int MemberId { get; set; }

        [Indexed(Name = "UX_SuicideKingsMember", Order = 2, Unique = true), NotNull]
        public string? Name { get; set; }

        public DateTimeOffset? Retired { get; set; }
    }
}
