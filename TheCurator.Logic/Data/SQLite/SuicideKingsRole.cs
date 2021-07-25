using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsRole
    {
        [Indexed(Name = "UX_SuicideKingsRole", Order = 1, Unique = true)]
        public int GuildId { get; set; }

        [Indexed(Name = "UX_SuicideKingsRole", Order = 1, Unique = true)]
        public int RoleId { get; set; }
    }
}
