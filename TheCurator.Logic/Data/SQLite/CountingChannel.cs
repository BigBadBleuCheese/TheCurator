using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class CountingChannel
    {
        [PrimaryKey]
        public int ChannelId { get; set; }

        public int Count { get; set; }

        public int LastAuthorId { get; set; }
    }
}
