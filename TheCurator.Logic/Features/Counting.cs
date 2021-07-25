using Cogs.Disposal;
using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheCurator.Logic.Data;

namespace TheCurator.Logic.Features
{
    public class Counting : SyncDisposable, IFeature
    {
        public Counting(IDataStore dataStore, IBot bot)
        {
            RequestIdentifiers = new string[] { "counting" };
            this.dataStore = dataStore;
            this.bot = bot;
            this.bot.Client.MessageReceived += ClientMessageReceived;
        }

        readonly IBot bot;
        readonly IDataStore dataStore;

        public string Description => "Manages a counting game in a channel";

        public IReadOnlyList<(string command, string description)> Examples => new (string command, string description)[]
        {
            ("enable", "Enables the counting game in the current channel"),
            ("disable", "Disables the counting game in the current channel"),
        };

        public string Name => "Counting";

        public IReadOnlyList<string> RequestIdentifiers { get; }

        async Task ClientMessageReceived(SocketMessage message)
        {
            if (message.Channel is IGuildChannel &&
                message.Author.Id != bot.Client.CurrentUser.Id &&
                double.TryParse(message.Content, out var number) &&
                number == Math.Truncate(number))
            {
                var (nullableCurrentCount, nullableLastAuthorId) = await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false);
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

        protected override bool Dispose(bool disposing)
        {
            if (disposing)
                bot.Client.MessageReceived -= ClientMessageReceived;
            return true;
        }

        public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
        {
            if (bot.IsAdministrativeUser(message.Author) && commandArgs.Count == 2 && RequestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase))
            {
                var secondArgument = commandArgs[1].ToUpperInvariant();
                if (secondArgument == "ENABLE")
                {
                    if ((await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false)).count is null)
                    {
                        await dataStore.SetCountingChannelCountAsync(message.Channel.Id, 0, 0).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("This channel is equipped for counting.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    else
                        await message.Channel.SendMessageAsync("Your request cannot be processed. This channel is already equipped for counting.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                else if (secondArgument == "DISABLE")
                {
                    if ((await dataStore.GetCountingChannelCountAsync(message.Channel.Id).ConfigureAwait(false)).count is not null)
                    {
                        await dataStore.SetCountingChannelCountAsync(message.Channel.Id, null, null).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("Counting in this channel is no longer operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    else
                        await message.Channel.SendMessageAsync("Your request cannot be processed. Counting in this channel is not operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
            }
            return false;
        }
    }
}
