namespace TheCurator.Logic.Features;

public interface IFeature :
    IDisposable
{
    string Description { get; }

    IReadOnlyList<(string command, string description)> Examples { get; }

    string Name { get; }

    IReadOnlyList<string> RequestIdentifiers { get; }

    Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs);
}
