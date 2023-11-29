namespace TheCurator.Logic.Features;

public class Choose :
    IFeature
{
    public Choose(IBot bot) =>
        this.bot = bot;

    readonly IBot bot;
    IApplicationCommand? choose;

    public string Description =>
        "Magic 8-Ball";

    public string Name =>
        "Choose";

    public async Task CreateGlobalApplicationCommandsAsync()
    {
        choose = await bot.Client.CreateGlobalApplicationCommandAsync(new SlashCommandBuilder()
            .WithName("choose")
            .WithDescription("Magic 8-Ball")
            .AddOption("choices", ApplicationCommandOptionType.String, "The choices from which to choose", true)
            .Build()).ConfigureAwait(false);
    }

    public void Dispose()
    {
    }

    public async Task ProcessCommandAsync(SocketSlashCommand command)
    {
        if (command.CommandId == choose?.Id)
            await command.RespondAsync(Random.Shared.GetItems(Bot.GetRequestArguments(command.Data.Options.First().Value.ToString()!).ToArray(), 1)[0]);
    }
}
