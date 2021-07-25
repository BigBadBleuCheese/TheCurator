using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class SuicideKingsDropWitness
    {
        [Indexed(Name = "UX_SuicideKingsDropWitness", Order = 1, Unique = true), NotNull]
        public int MemberId { get; set; }

        [Indexed(Name = "UX_SuicideKingsDropWitness", Order = 2, Unique = true), NotNull]
        public int DropId { get; set; }
    }
}
