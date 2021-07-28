using SQLite;

namespace TheCurator.Logic.Data.SQLite
{
    public class PollVote
    {
        [Indexed(Name = "UX_PollVote", Order = 1, Unique = true), NotNull]
        public int OptionId { get; set; }

        [Indexed(Name = "UX_PollVote", Order = 2, Unique = true), NotNull]
        public long UserId { get; set; }
    }
}
