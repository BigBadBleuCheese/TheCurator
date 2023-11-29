namespace TheCurator.Logic.Features;

public class Counting :
    SyncDisposable,
    IFeature
{
    public Counting(IDataStore dataStore, IBot bot)
    {
        this.dataStore = dataStore;
        this.bot = bot;
        this.bot.Client.MessageReceived += ClientMessageReceived;
    }

    readonly IBot bot;
    readonly IDataStore dataStore;
    IApplicationCommand? toggle;

    public string Description =>
        "Manages a counting game in a channel";

    public string Name =>
        "Counting";

    async Task ClientMessageReceived(SocketMessage message)
    {
        if (message.Channel is IGuildChannel &&
            !message.Author.IsBot &&
            double.TryParse(message.Content, out var number) &&
            number == Math.Truncate(number))
        {
            var (nullableCurrentCount, nullableLastAuthorId) = await dataStore.GetCountingChannelCountAsync(message.Channel.Id);
            if (nullableCurrentCount is { } currentCount && nullableLastAuthorId is { } lastAuthorId)
            {
                var intNumber = (int)number;
                if (lastAuthorId != message.Author.Id && intNumber - 1 == currentCount)
                {
                    await dataStore.SetCountingChannelCountAsync(message.Channel.Id, intNumber, message.Author.Id);
                    await message.AddReactionAsync(new Emoji("✅"));
                }
                else
                {
                    await dataStore.SetCountingChannelCountAsync(message.Channel.Id, 0, 0);
                    await message.Channel.SendMessageAsync($"The counting rules will be strictly enforced. The count was ruined at **{currentCount}**. The next number is **1**.", messageReference: new MessageReference(message.Id));
                }
            }
        }
    }

    public async Task CreateGlobalApplicationCommandsAsync()
    {
        toggle = await bot.Client.CreateGlobalApplicationCommandAsync(new SlashCommandBuilder()
            .WithName("counting")
            .WithDescription("Manages the counting game in this channel")
            .AddOption("enabled", ApplicationCommandOptionType.Boolean, "Whether the counting game is enabled in this channel", true)
            .Build());
    }

    protected override bool Dispose(bool disposing)
    {
        if (disposing)
            bot.Client.MessageReceived -= ClientMessageReceived;
        return true;
    }

    public async Task ProcessCommandAsync(SocketSlashCommand command)
    {
        if (await command.RequireAdministrativeUserAsync(bot) && command.Data.Options.First().Value is bool enable)
        {
            await command.DeferAsync();
            if (enable)
            {
                if ((await dataStore.GetCountingChannelCountAsync(command.Channel.Id)).count is null)
                {
                    await dataStore.SetCountingChannelCountAsync(command.Channel.Id, 0, 0);
                    await command.FollowupAsync("This channel is equipped for counting.", ephemeral: true);
                }
                else
                    await command.FollowupAsync("Your request cannot be processed. This channel is already equipped for counting.", ephemeral: true);
            }
            else
            {
                if ((await dataStore.GetCountingChannelCountAsync(command.Channel.Id)).count is not null)
                {
                    await dataStore.SetCountingChannelCountAsync(command.Channel.Id, null, null);
                    await command.FollowupAsync("Counting in this channel is no longer operational.", ephemeral: true);
                }
                else
                    await command.FollowupAsync("Your request cannot be processed. Counting in this channel is not operational.", ephemeral: true);
            }
        }
    }
}
