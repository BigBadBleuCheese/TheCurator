namespace TheCurator.Logic.Features;

public class PollBuilder
{
    public PollBuilder(ulong authorId) =>
        AuthorId = authorId;

    public int AllowedVotes { get; set; } = 1;

    public ulong AuthorId { get; }

    public TimeSpan? Duration
    {
        get => End is { } end ? end - Start : null;
        set => End = value is { } duration ? Start + duration : null;
    }

    public DateTimeOffset? End { get; set; }

    public ulong GuildId { get; }

    public bool IsSecretBallot { get; set; }

    public List<string> Options { get; } = new List<string>();

    public string? Question { get; set; }

    public List<ulong> RoleIds { get; } = new List<ulong>();

    public DateTimeOffset Start { get; set; } = DateTimeOffset.UtcNow;

    public PollBuilderState State { get; set; } = PollBuilderState.None;
}
