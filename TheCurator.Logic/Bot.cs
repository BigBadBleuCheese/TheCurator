namespace TheCurator.Logic;

public class Bot :
    SyncDisposable,
    IBot
{
    public Bot(ILifetimeScope lifetimeScope, IDataStore dataStore, IFeatureCatalog featureCatalog)
    {
        this.dataStore = dataStore;
        this.lifetimeScope = lifetimeScope.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(this).As<IBot>().ExternallyOwned();
            builder.RegisterInstance(this.dataStore).As<IDataStore>().ExternallyOwned();
            foreach (var type in featureCatalog.Services)
                builder.RegisterType(type);
        });
        Client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });
        features = featureCatalog.Services.Select(type => this.lifetimeScope.Resolve(type)).OfType<IFeature>().ToImmutableArray();
    }

    readonly IDataStore dataStore;
    readonly IEnumerable<IFeature> features;
    readonly AsyncLock initializationAccess = new();
    bool isInitialized = false;
    readonly ILifetimeScope lifetimeScope;

    public DiscordSocketClient Client { get; }

    protected override bool Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var feature in features)
                feature.Dispose();
            Client.Connected -= ConnectedAsync;
            Client.Disconnected -= DisconnectedAsync;
            Client.Ready -= ReadyAsync;
            Client.SlashCommandExecuted -= SlashCommandExecutedAsync;
            Client.Dispose();
            lifetimeScope.Dispose();
        }
        return false;
    }

    public async Task InitializeAsync(string token)
    {
        using (await initializationAccess.LockAsync().ConfigureAwait(false))
        {
            if (isInitialized)
                throw new InvalidOperationException();
            Client.Connected += ConnectedAsync;
            Client.Disconnected += DisconnectedAsync;
            Client.Ready += ReadyAsync;
            Client.SlashCommandExecuted += SlashCommandExecutedAsync;
            await Client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await Client.StartAsync().ConfigureAwait(false);
            isInitialized = true;
        }
    }

    async Task ConnectedAsync() =>
        await dataStore.ConnectAsync();

    async Task DisconnectedAsync(Exception ex) =>
        await dataStore.DisconnectAsync();

    async Task GuildAvailableAsync(SocketGuild arg)
    {
        foreach (var feature in features)
            await feature.CreateGlobalApplicationCommandsAsync().ConfigureAwait(false);
    }

    public bool IsAdministrativeUser(IUser user) =>
        user is IGuildUser guildUser && guildUser.Guild.OwnerId == user.Id;

    async Task ReadyAsync()
    {
        foreach (var feature in features)
            await feature.CreateGlobalApplicationCommandsAsync().ConfigureAwait(false);
    }

    Task SlashCommandExecutedAsync(SocketSlashCommand command)
    {
        _ = Task.Run(async () =>
        {
            foreach (var feature in features)
                await feature.ProcessCommandAsync(command);
        });
        return Task.CompletedTask;
    }

    public static EmbedAuthorBuilder GetEmbedAuthorBuilder() =>
        new()
        {
            Name = "The Curator",
            Url = "https://github.com/BigBadBleuCheese/TheCurator",
            IconUrl = "https://raw.githubusercontent.com/BigBadBleuCheese/TheCurator/master/The_Curator.jpg"
        };

    public static IEnumerable<string> GetRequestArguments(string request)
    {
        string text;
        var argument = new StringBuilder();
        var isQuoted = false;
        char? lastChar = null;
        for (var i = 0; i < request.Length; ++i)
        {
            var character = request[i];
            if (isQuoted)
            {
                if (character == '\"')
                    isQuoted = false;
                else
                    argument.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                text = argument.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return argument.ToString();
                    argument.Clear();
                    lastChar = null;
                }
            }
            else if (character == '\"')
            {
                isQuoted = true;
                if (lastChar == '\"')
                    argument.Append('\"');
            }
            else
                argument.Append(character);
            lastChar = character;
        }
        text = argument.ToString();
        if (!string.IsNullOrWhiteSpace(text))
            yield return argument.ToString();
    }
}
