using Cogs.Disposal;
using Cogs.Exceptions;
using Discord;
using Discord.WebSocket;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheCurator.Logic.Data;

namespace TheCurator.Logic
{
    public class Bot : SyncDisposable, IBot
    {
        public Bot(IDataStore dataStore) => this.dataStore = dataStore;

        readonly DiscordSocketClient client = new DiscordSocketClient();
        readonly IDataStore dataStore;
        readonly AsyncLock initializationAccess = new AsyncLock();
        bool isInitialized = false;

        protected override bool Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Connected -= ConnectedAsync;
                client.Disconnected -= DisconnectedAsync;
                client.GuildAvailable -= GuildAvailableAsync;
                client.GuildUnavailable -= GuildUnavailableAsync;
                client.GuildUpdated -= GuildUpdatedAsync;
                client.JoinedGuild -= JoinedGuildAsync;
                client.LeftGuild -= LeftGuildAsync;
                client.Log -= LogAsync;
                client.LoggedIn -= LoggedInAsync;
                client.LoggedOut -= LoggedOutAsync;
                client.MessageReceived -= MessageReceivedAsync;
                client.ReactionAdded -= ReactionAddedAsync;
                client.ReactionRemoved -= ReactionRemovedAsync;
                client.ReactionsCleared -= ReactionsClearedAsync;
                client.ReactionsRemovedForEmote -= ReactionsRemovedForEmoteAsync;
                client.Ready -= ReadyAsync;
                client.UserUpdated -= UserUpdatedAsync;
                client.Dispose();
            }
            return false;
        }

        public async Task InitializeAsync(string token)
        {
            using (await initializationAccess.LockAsync().ConfigureAwait(false))
            {
                if (isInitialized)
                    throw new InvalidOperationException();
                client.Connected += ConnectedAsync;
                client.Disconnected += DisconnectedAsync;
                client.GuildAvailable += GuildAvailableAsync;
                client.GuildUnavailable += GuildUnavailableAsync;
                client.GuildUpdated += GuildUpdatedAsync;
                client.JoinedGuild += JoinedGuildAsync;
                client.LeftGuild += LeftGuildAsync;
                client.Log += LogAsync;
                client.LoggedIn += LoggedInAsync;
                client.LoggedOut += LoggedOutAsync;
                client.MessageReceived += MessageReceivedAsync;
                client.ReactionAdded += ReactionAddedAsync;
                client.ReactionRemoved += ReactionRemovedAsync;
                client.ReactionsCleared += ReactionsClearedAsync;
                client.ReactionsRemovedForEmote += ReactionsRemovedForEmoteAsync;
                client.Ready += ReadyAsync;
                client.UserUpdated += UserUpdatedAsync;
                await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
                await client.StartAsync().ConfigureAwait(false);
                isInitialized = true;
            }
        }

        #region Client Event Handlers

        async Task ConnectedAsync() => await dataStore.ConnectAsync();

        async Task DisconnectedAsync(Exception ex)
        {
            Console.Error.WriteLine($"Encountered unexpected exception while connected.{Environment.NewLine}{Environment.NewLine}{ex.GetFullDetails()}");
            await dataStore.DisconnectAsync();
        }

        Task GuildAvailableAsync(SocketGuild guild)
        {
            return Task.CompletedTask;
        }

        Task GuildUnavailableAsync(SocketGuild guild)
        {
            return Task.CompletedTask;
        }

        Task GuildUpdatedAsync(SocketGuild from, SocketGuild to)
        {
            return Task.CompletedTask;
        }

        bool IsUserAllowedToCommand(IUser user) => user is IGuildUser guildUser && guildUser.Guild.OwnerId == user.Id;

        Task JoinedGuildAsync(SocketGuild guild)
        {
            return Task.CompletedTask;
        }

        Task LeftGuildAsync(SocketGuild guild)
        {
            return Task.CompletedTask;
        }

        Task LogAsync(LogMessage logMessage)
        {
            return Task.CompletedTask;
        }

        Task LoggedInAsync()
        {
            return Task.CompletedTask;
        }

        Task LoggedOutAsync()
        {
            return Task.CompletedTask;
        }

        async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Channel is IGuildChannel guildChannel)
            {
                var currentGuildUser = await guildChannel.Guild.GetCurrentUserAsync();
                if (currentGuildUser is SocketGuildUser currentSocketGuildUser && new string[] { $"!{currentGuildUser.Id}" }.Concat(currentSocketGuildUser.Roles.Select(r => $"&{r.Id}")).Select(id => $"<@{id}>").FirstOrDefault(ap => message.Content.StartsWith(ap)) is { } atPrefix)
                {
                    var commandArgs = GetCommandArguments(message.Content.Substring(atPrefix.Length)).ToImmutableArray();
                    if (commandArgs.Length >= 1)
                    {
                        var firstArgument = commandArgs[0];
                        if (firstArgument.ToUpperInvariant() == "CHOOSE" && commandArgs.Length >= 2)
                        {
                            var choices = commandArgs.Skip(1).ToImmutableArray();
                            await message.Channel.SendMessageAsync(choices[new Random().Next(choices.Length)], messageReference: new MessageReference(message.Id));
                        }
                        else if (firstArgument.ToUpperInvariant() == "DISABLE" && commandArgs.Length >= 2 && commandArgs[1] is { } disableFeature)
                        {
                            if (disableFeature.ToUpperInvariant() == "COUNTING")
                            {
                                if (IsUserAllowedToCommand(message.Author))
                                {
                                    if ((await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false)).count is not null)
                                    {
                                        await dataStore.SetCountingChannelCountAsync(message.Channel.Id, null, null).ConfigureAwait(false);
                                        await message.Channel.SendMessageAsync("Counting in this channel is no longer operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    }
                                    else
                                        await message.Channel.SendMessageAsync("Your request cannot be processed. Counting in this channel is not operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                }
                                else
                                    await RejectCommandAsync(message);
                            }
                        }
                        else if (firstArgument.ToUpperInvariant() == "ENABLE" && commandArgs.Length >= 2 && commandArgs[1] is { } enableFeature)
                        {
                            if (enableFeature.ToUpperInvariant() == "COUNTING")
                            {
                                if (IsUserAllowedToCommand(message.Author))
                                {
                                    if ((await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false)).count is null)
                                    {
                                        await dataStore.SetCountingChannelCountAsync(message.Channel.Id, 0, 0).ConfigureAwait(false);
                                        await message.Channel.SendMessageAsync("This channel is equipped for counting.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    }
                                    else
                                        await message.Channel.SendMessageAsync("Your request cannot be processed. This channel is already equipped for counting.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                }
                                else
                                    await RejectCommandAsync(message);
                            }
                        }
                        else
                            await RejectCommandAsync(message);
                    }
                    else
                        await RejectCommandAsync(message);
                }
                if (double.TryParse(message.Content, out var number) &&
                    number == Math.Truncate(number))
                {
                    var (nullableCurrentCount, nullableLastAuthorId) = await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false);
                    if (nullableCurrentCount is { } currentCount && nullableLastAuthorId is { } lastAuthorId)
                    {
                        var uintNumber = (uint)number;
                        if (lastAuthorId != message.Author.Id && uintNumber - 1 == currentCount)
                        {
                            await dataStore.SetCountingChannelCountAsync(message.Channel.Id, uintNumber, message.Author.Id);
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
        }

        Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel messageChannel, SocketReaction reaction)
        {
            return Task.CompletedTask;
        }

        Task ReactionRemovedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel messageChannel, SocketReaction reaction)
        {
            return Task.CompletedTask;
        }

        Task ReactionsClearedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel messageChannel)
        {
            return Task.CompletedTask;
        }

        Task ReactionsRemovedForEmoteAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel messageChannel, IEmote emote)
        {
            return Task.CompletedTask;
        }

        Task ReadyAsync()
        {
            return Task.CompletedTask;
        }

        Task RejectCommandAsync(SocketMessage message) =>
            message.Channel.SendMessageAsync("Your request cannot be processed.", messageReference: new MessageReference(message.Id));

        Task UserUpdatedAsync(SocketUser from, SocketUser to)
        {
            return Task.CompletedTask;
        }

        #endregion Client Event Handlers

        static IEnumerable<string> GetCommandArguments(string command)
        {
            string text;
            var argument = new StringBuilder();
            var isQuoted = false;
            for (var i = 0; i < command.Length; ++i)
            {
                var character = command[i];
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
                    }
                }
                else if (character == '\"')
                    isQuoted = true;
                else
                    argument.Append(character);
            }
            text = argument.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                yield return argument.ToString();
        }
    }
}
