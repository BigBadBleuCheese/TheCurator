using SQLite;
using System;

namespace TheCurator.Logic.Data.SQLite
{
    public class Poll
    {
        [NotNull]
        public int AllowedVotes { get; set; }

        [NotNull]
        public long AuthorId { get; set; }

        [NotNull]
        public long ChannelId { get; set; }

        public DateTimeOffset? End { get; set; }

        [Indexed, NotNull]
        public long GuildId { get; set; }

        [NotNull]
        public bool IsSecretBallot { get; set; }

        [Indexed]
        public long? MessageId { get; set; }

        [PrimaryKey, AutoIncrement]
        public int PollId { get; set; }

        [NotNull]
        public string? Question { get; set; }

        [NotNull]
        public DateTimeOffset Start { get; set; }
    }
}
