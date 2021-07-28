using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class PollingRole
    {
        [Indexed(Name = "UX_PollChannelRole", Order = 1, Unique = true), NotNull]
        public long ChannelId { get; set; }

        [Indexed(Name = "UX_PollChannelRole", Order = 1, Unique = true), NotNull]
        public long RoleId { get; set; }
    }
}
