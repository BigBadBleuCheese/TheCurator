using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class PollRole
    {
        [Indexed(Name = "UX_PollRole", Order = 1, Unique = true), NotNull]
        public int PollId { get; set; }

        [Indexed(Name = "UX_PollRole", Order = 2, Unique = true), NotNull]
        public long RoleId { get; set; }
    }
}
