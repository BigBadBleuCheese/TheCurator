namespace TheCurator.Logic.Features;

public enum PollBuilderState
{
    None,
    Question,
    Options,
    AllowedVotes,
    Roles,
    Start,
    Duration,
    End
}
