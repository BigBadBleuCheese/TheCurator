namespace TheCurator.Logic.Features;

public interface IFeature :
    IDisposable
{
    Task CreateGlobalApplicationCommandsAsync();

    string Description { get; }

    string Name { get; }

    Task ProcessCommandAsync(SocketSlashCommand command);
}
