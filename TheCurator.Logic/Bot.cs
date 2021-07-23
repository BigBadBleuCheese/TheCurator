using Autofac;
using Cogs.Disposal;
using Discord;
using Discord.WebSocket;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheCurator.Logic.Data;
using TheCurator.Logic.Features;

namespace TheCurator.Logic
{
    public class Bot : SyncDisposable, IBot
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
            features = featureCatalog.Services.Select(type => this.lifetimeScope.Resolve(type)).OfType<IFeature>().ToImmutableArray();
            Client = new DiscordSocketClient();
        }
        readonly IDataStore dataStore;
        readonly IEnumerable<IFeature> features;
        readonly AsyncLock initializationAccess = new AsyncLock();
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
                Client.MessageReceived -= MessageReceivedAsync;
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
                Client.MessageReceived += MessageReceivedAsync;
                await Client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
                await Client.StartAsync().ConfigureAwait(false);
                isInitialized = true;
            }
        }

        async Task ConnectedAsync() => await dataStore.ConnectAsync();

        async Task DisconnectedAsync(Exception ex) => await dataStore.DisconnectAsync();

        public bool IsAdministrativeUser(IUser user) => user is IGuildUser guildUser && guildUser.Guild.OwnerId == user.Id;

        async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.Id != Client.CurrentUser.Id && message.Channel is IGuildChannel guildChannel)
            {
                var currentGuildUser = await guildChannel.Guild.GetCurrentUserAsync();
                if (currentGuildUser is SocketGuildUser currentSocketGuildUser && new string[] { $"!{currentGuildUser.Id}" }.Concat(currentSocketGuildUser.Roles.Select(r => $"&{r.Id}")).Select(id => $"<@{id}>").FirstOrDefault(ap => message.Content.StartsWith(ap)) is { } atPrefix)
                {
                    var requestArgs = GetRequestArguments(message.Content.Substring(atPrefix.Length)).ToImmutableArray();
                    var requestProcessed = false;
                    foreach (var feature in features)
                    {
                        if (await feature.ProcessRequestAsync(message, requestArgs))
                        {
                            requestProcessed = true;
                            break;
                        }
                    }
                    if (!requestProcessed)
                        await message.Channel.SendMessageAsync("Your request cannot be processed.", messageReference: new MessageReference(message.Id));
                }
            }
        }

        static IEnumerable<string> GetRequestArguments(string request)
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
}
