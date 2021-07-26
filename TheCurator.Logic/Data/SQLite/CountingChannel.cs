using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class CountingChannel
    {
        [PrimaryKey]
        public long ChannelId { get; set; }

        public int Count { get; set; }

        public long LastAuthorId { get; set; }
    }
}
