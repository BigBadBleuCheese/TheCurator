using SQLite;
using System;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsDrop
    {
        [NotNull]
        public int ListId { get; set; }

        [NotNull]
        public int MemberId { get; set; }

        public string? Reason { get; set; }

        [PrimaryKey, AutoIncrement]
        public int DropId { get; set; }

        [NotNull]
        public DateTimeOffset TimeStamp { get; set; }
    }
}
