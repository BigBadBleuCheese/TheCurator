using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class Snowflake
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed, NotNull]
        public string? DiscordId { get; set; }
    }
}
