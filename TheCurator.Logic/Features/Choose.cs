namespace TheCurator.Logic.Features;

public class Choose :
    IFeature
{
    public Choose() =>
        RequestIdentifiers = new string[] { "choose" };

    public string Description =>
        "Magic 8-Ball";

    public IReadOnlyList<(string command, string description)> Examples =>
        new (string command, string description)[]
        {
            ("choose [Option 1] ... [Option n]", "Selects one of the options at random and replies with the option selected"),
        };

    public string Name =>
        "Choose";

    public IReadOnlyList<string> RequestIdentifiers { get; }

    public void Dispose()
    {
    }

    public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count >= 2 && RequestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase))
        {
            var choices = commandArgs.Skip(1).ToImmutableArray();
            await message.Channel.SendMessageAsync(choices[new Random().Next(choices.Length)], messageReference: new MessageReference(message.Id));
            return true;
        }
        return false;
    }
}
