using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class Schema
    {
        [NotNull]
        public int Version { get; set; }
    }
}
