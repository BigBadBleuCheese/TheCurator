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
        {
            var choices = Bot.GetRequestArguments(command.Data.Options.First().Value.ToString()!).ToArray();
            await command.RespondAsync(embed: new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithFields(choices.Select((choice, index) => new EmbedFieldBuilder()
                    .WithName($"Choice {index + 1}")
                    .WithValue(choice)
                ).Concat(new EmbedFieldBuilder[] { new EmbedFieldBuilder()
                    .WithName("Selected Choice")
                    .WithValue(Random.Shared.GetItems(choices, 1)[0])
                }).ToArray())
                .Build());
        }
    }
}
