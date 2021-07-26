using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsRole
    {
        [Indexed(Name = "UX_SuicideKingsRole", Order = 1, Unique = true), NotNull]
        public long GuildId { get; set; }

        [Indexed(Name = "UX_SuicideKingsRole", Order = 1, Unique = true), NotNull]
        public long RoleId { get; set; }
    }
}
