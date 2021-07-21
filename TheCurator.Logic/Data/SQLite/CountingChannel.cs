using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class CountingChannel
    {
        [PrimaryKey]
        public string? ChannelId { get; set; }

        public uint Count { get; set; }

        public string? LastAuthorId { get; set; }
    }
}
