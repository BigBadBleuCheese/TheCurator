using Cogs.Disposal;
using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TheCurator.Logic.Data;

namespace TheCurator.Logic.Features
{
    public class Polling : SyncDisposable, IFeature
    {
        public Polling(IDataStore dataStore, IBot bot)
        {
            RequestIdentifiers = new string[] { "poll" };
            activePollIdByMessageId = new ConcurrentDictionary<ulong, (int pollId, DateTimeOffset? end)>();
            pollsUnderConstructionByMessageId = new ConcurrentDictionary<ulong, PollBuilder>();
            pollsAwaitingDisplay = new ConcurrentBag<(int pollId, DateTimeOffset start)>();
            this.dataStore = dataStore;
            this.bot = bot;
            var client = this.bot.Client;
            client.GuildAvailable += ClientGuildAvailable;
            client.MessageDeleted += ClientMessageDeleted;
            client.MessageReceived += ClientMessageReceived;
            client.ReactionAdded += ClientReactionAdded;
            client.ReactionRemoved += ClientReactionRemoved;
            client.ReactionsCleared += ClientReactionsCleared;
            client.ReactionsRemovedForEmote += ClientReactionsRemovedForEmote;
            timer = new Timer(TimerTick, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }

        readonly ConcurrentDictionary<ulong, (int pollId, DateTimeOffset? end)> activePollIdByMessageId;
        readonly IBot bot;
        readonly IDataStore dataStore;
        readonly ConcurrentDictionary<ulong, PollBuilder> pollsUnderConstructionByMessageId;
        readonly ConcurrentBag<(int pollId, DateTimeOffset start)> pollsAwaitingDisplay;
        readonly Timer timer;

        public string Description => "Allows the creation and tallying of the results of polls";

        public IReadOnlyList<(string command, string description)> Examples => new (string command, string description)[]
        {
            ("role add [Role Name]", "Adds a Discord role to the collection of those on the Discord allowed to add polls to this channel"),
            ("role remove [Role Name]", "Removes a Discord role from the collection of those on the Discord allowed to add polls to this channel"),
            ("add", "Begins the process of adding a new poll")
        };

        public string Name => "Polling";

        public IReadOnlyList<string> RequestIdentifiers { get; }

        async Task ClientGuildAvailable(SocketGuild guild)
        {
            foreach (var poll in await dataStore.GetOpenOrPendingPollsForGuildAsync(guild.Id).ConfigureAwait(false))
                pollsAwaitingDisplay.Add(poll);
        }

        async Task ClientMessageDeleted(Cacheable<IMessage, ulong> cacheableMessage, ISocketMessageChannel channel)
        {
            if (activePollIdByMessageId.TryRemove(cacheableMessage.Id, out var activePoll))
            {
                var guildChannel = (SocketGuildChannel)channel;
                var (authorId, _, _, _, question, options, roleIds, allowedVotes, isSecretBallot, _, end) = await dataStore.GetPollAsync(activePoll.pollId).ConfigureAwait(false);
                var results = await dataStore.GetPollResultsAsync(activePoll.pollId).ConfigureAwait(false);
                if (end is null)
                {
                    await dataStore.ClosePollAsync(activePoll.pollId).ConfigureAwait(false);
                    end ??= DateTimeOffset.UtcNow;
                }
                var embedFields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder
                    {
                        Name = "Author",
                        Value = $"This question was posed by <@!{authorId}>.",
                        IsInline = true
                    }
                };
                if (roleIds.Any())
                    embedFields.Add(new EmbedFieldBuilder
                    {
                        Name = "Role Restriction",
                        Value = $"Only users that were a member of at least one of these roles could vote: {string.Join(", ", roleIds.Select(roleId => $"<@&{roleId}>"))}",
                        IsInline = true
                    });
                if (allowedVotes == 0)
                    embedFields.Add(new EmbedFieldBuilder
                    {
                        Name = "Unlimited Votes",
                        Value = "Those voting could vote once for as many options as they wished.",
                        IsInline = true
                    });
                else if (allowedVotes > 1)
                    embedFields.Add(new EmbedFieldBuilder
                    {
                        Name = "Multiple Votes",
                        Value = $"Those voting could vote for up to {allowedVotes:n0} options.",
                        IsInline = true
                    });
                var embedBuilder = new EmbedBuilder
                {
                    Author = Bot.GetEmbedAuthorBuilder(),
                    Title = $"Results: {question}",
                    Timestamp = end,
                    Fields = embedFields
                };
                int? tiedVoteCount = null;
                if (allowedVotes > 0)
                {
                    var descendingVoteCounts = options
                        .Select(option => results.TryGetValue(option.id, out var voters) ? voters.Count : 0)
                        .OrderByDescending(count => count)
                        .Skip(allowedVotes - 1)
                        .Take(2)
                        .ToImmutableArray();
                    if (descendingVoteCounts.Length == 2 && descendingVoteCounts[0] == descendingVoteCounts[1])
                        tiedVoteCount = descendingVoteCounts[0];
                }
                if (isSecretBallot)
                    embedBuilder.Description = string.Join
                    (
                        "\n",
                        options
                            .OrderByDescending(option => results.TryGetValue(option.id, out var voters) ? voters.Count : 0)
                            .Select((option, index) =>
                            {
                                if (!results.TryGetValue(option.id, out var voters))
                                    voters = new ulong[0];
                                return $"{(allowedVotes == 0 ? "•" : voters.Count == tiedVoteCount ? "🤷‍♂️" : index < allowedVotes ? "✅" : "❌")} {option.name}: {voters.Count:n0} vote{(voters.Count == 1 ? string.Empty : "s")}";
                            })
                    );
                else
                    embedBuilder.Description = string.Join
                        (
                        "\n",
                        options
                            .OrderByDescending(option => results.TryGetValue(option.id, out var voters) ? voters.Count : 0)
                            .Select((option, index) =>
                            {
                                if (!results.TryGetValue(option.id, out var voters))
                                    voters = new ulong[0];
                                return $"{(allowedVotes == 0 ? "•" : voters.Count == tiedVoteCount ? "🤷‍♂️" : index < allowedVotes ? "✅" : "❌")} {option.name}: {voters.Count:n0} vote{(voters.Count == 1 ? string.Empty : "s")}{(voters.Count == 0 ? string.Empty : $" ({string.Join(", ", voters.Select(userId => guildChannel.Guild.GetUser(userId)).OrderBy(user => user.Nickname).Select(user => $"<@!{user.Id}>"))})")}";
                            })
                        );
                var sentMessage = await channel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
                var distinctVoters = results.SelectMany(kv => kv.Value).Distinct();
                foreach (var voter in distinctVoters)
                    await guildChannel.Guild.GetUser(voter).SendMessageAsync($"A poll in which you cast a vote has concluded. To see the results, click here: {sentMessage.GetJumpUrl()}").ConfigureAwait(false);
                if (!distinctVoters.Contains(authorId))
                    await guildChannel.Guild.GetUser(authorId).SendMessageAsync($"A poll you created has concluded. To see the results, click here: {sentMessage.GetJumpUrl()}").ConfigureAwait(false);
            }
        }

        async Task ClientMessageReceived(SocketMessage message)
        {
            if (message.Reference?.MessageId.ToNullable() is { } referencedMessageId &&
                pollsUnderConstructionByMessageId.TryGetValue(referencedMessageId, out var pollBuilder) &&
                await message.Channel.GetMessageAsync(referencedMessageId).ConfigureAwait(false) is IUserMessage pollBuilderMessage)
            {
                if (message.Author.Id == pollBuilder.AuthorId)
                {
                    var messageContent = message.Content.Trim();
                    var stateChanged = false;
                    switch (pollBuilder.State)
                    {
                        case PollBuilderState.AllowedVotes:
                            if (int.TryParse(messageContent, out var allowedVotes))
                            {
                                pollBuilder.AllowedVotes = allowedVotes;
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.Duration:
                            if (messageContent.Equals("infinite", StringComparison.OrdinalIgnoreCase))
                            {
                                pollBuilder.Duration = null;
                                stateChanged = true;
                            }
                            if (TimeSpan.TryParse(messageContent, out var duration) && duration > TimeSpan.Zero)
                            {
                                pollBuilder.Duration = duration;
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.End:
                            if (messageContent.Equals("none", StringComparison.OrdinalIgnoreCase))
                            {
                                pollBuilder.End = null;
                                stateChanged = true;
                            }
                            if (DateTimeOffset.TryParse(messageContent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end) && end > pollBuilder.Start)
                            {
                                pollBuilder.End = end;
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.Options:
                            if (!string.IsNullOrWhiteSpace(messageContent))
                            {
                                pollBuilder.Options.Clear();
                                pollBuilder.Options.AddRange(Bot.GetRequestArguments(messageContent).Take(optionEmoteChars.Length));
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.Question:
                            if (!string.IsNullOrWhiteSpace(messageContent))
                            {
                                pollBuilder.Question = messageContent;
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.Roles:
                            if (!string.IsNullOrWhiteSpace(messageContent) && message.Channel is SocketGuildChannel guildChannel)
                            {
                                var guildRolesByName = guildChannel.Guild.Roles
                                    .GroupBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
                                pollBuilder.RoleIds.Clear();
                                pollBuilder.RoleIds.AddRange
                                (
                                    Bot.GetRequestArguments(messageContent)
                                        .Select(roleName => guildRolesByName.TryGetValue(roleName, out var roleId) ? roleId : (ulong?)null)
                                        .Where(roleId => roleId is not null)
                                        .Select(nullableRoleId => nullableRoleId!.Value)
                                );
                                stateChanged = true;
                            }
                            break;
                        case PollBuilderState.Start:
                            if (messageContent.Equals("now", StringComparison.OrdinalIgnoreCase))
                            {
                                pollBuilder.Start = DateTimeOffset.UtcNow;
                                stateChanged = true;
                            }
                            if (DateTimeOffset.TryParse(messageContent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start) && (pollBuilder.End is null || start < pollBuilder.End))
                            {
                                pollBuilder.Start = start;
                                stateChanged = true;
                            }
                            break;
                    }
                    if (stateChanged)
                    {
                        pollBuilder.State = PollBuilderState.None;
                        await RenderPollBuilderMessageAsync(pollBuilderMessage, pollBuilder).ConfigureAwait(false);
                    }
                }
                await message.DeleteAsync().ConfigureAwait(false);
            }
        }

        async Task ClientReactionAdded(Cacheable<IUserMessage, ulong> cacheableMessage, ISocketMessageChannel channel, SocketReaction reaction)
        {
            var guildChannel = channel as SocketGuildChannel;
            if (pollsUnderConstructionByMessageId.TryGetValue(cacheableMessage.Id, out var pollBuilder) &&
                await channel.GetMessageAsync(cacheableMessage.Id).ConfigureAwait(false) is IUserMessage pollBuilderMessage &&
                guildChannel is not null)
            {
                if (reaction.UserId == pollBuilder.AuthorId)
                {
                    var stateChanged = false;
                    var name = reaction.Emote.Name;
                    if (name == "❓")
                    {
                        pollBuilder.State = PollBuilderState.Question;
                        stateChanged = true;
                    }
                    else if (name == "✅")
                    {
                        pollBuilder.State = PollBuilderState.Options;
                        stateChanged = true;
                    }
                    else if (name == "#️⃣")
                    {
                        pollBuilder.State = PollBuilderState.AllowedVotes;
                        stateChanged = true;
                    }
                    else if (name == "🧑")
                    {
                        pollBuilder.State = PollBuilderState.Roles;
                        stateChanged = true;
                    }
                    else if (name == "🔒")
                    {
                        pollBuilder.IsSecretBallot = !pollBuilder.IsSecretBallot;
                        stateChanged = true;
                    }
                    else if (name == "▶")
                    {
                        pollBuilder.State = PollBuilderState.Start;
                        stateChanged = true;
                    }
                    else if (name == "↔")
                    {
                        pollBuilder.State = PollBuilderState.Duration;
                        stateChanged = true;
                    }
                    else if (name == "⏹")
                    {
                        pollBuilder.State = PollBuilderState.End;
                        stateChanged = true;
                    }
                    else if (name == "💾")
                    {
                        if (string.IsNullOrWhiteSpace(pollBuilder.Question))
                            await guildChannel.Guild.GetUser(reaction.UserId).SendMessageAsync($"You have not yet provided a question for your poll. React to the poll building message with ❓ to do so. To go back to the poll building message, click here: {pollBuilderMessage.GetJumpUrl()}").ConfigureAwait(false);
                        else if (pollBuilder.Options.Count == 0)
                            await guildChannel.Guild.GetUser(reaction.UserId).SendMessageAsync($"You have not yet provided options for your poll. React to the poll building message with ✅ to do so. To go back to the poll building message, click here: {pollBuilderMessage.GetJumpUrl()}").ConfigureAwait(false);
                        else
                        {
                            pollsUnderConstructionByMessageId.TryRemove(pollBuilderMessage.Id, out _);
                            await pollBuilderMessage.DeleteAsync().ConfigureAwait(false);
                            pollsAwaitingDisplay.Add((await dataStore.AddPollAsync
                            (
                                pollBuilder.AuthorId,
                                guildChannel.Guild.Id,
                                guildChannel.Id,
                                pollBuilder.Question!,
                                pollBuilder.Options.Select((option, index) => (option, optionEmoteChars[index])).ToImmutableArray(),
                                pollBuilder.RoleIds,
                                pollBuilder.AllowedVotes,
                                pollBuilder.IsSecretBallot,
                                pollBuilder.Start,
                                pollBuilder.End
                            ).ConfigureAwait(false), pollBuilder.Start));
                        }
                    }
                    else if (name == "❌")
                    {
                        if (pollBuilder.State == PollBuilderState.None)
                        {
                            pollsUnderConstructionByMessageId.TryRemove(pollBuilderMessage.Id, out _);
                            await pollBuilderMessage.DeleteAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            pollBuilder.State = PollBuilderState.None;
                            stateChanged = true;
                        }
                    }
                    if (stateChanged)
                        await RenderPollBuilderMessageAsync(pollBuilderMessage, pollBuilder).ConfigureAwait(false);
                }
                if (reaction.UserId != bot.Client.CurrentUser.Id)
                    await pollBuilderMessage.RemoveReactionAsync(reaction.Emote, guildChannel.GetUser(reaction.UserId)).ConfigureAwait(false);
            }
            else if (activePollIdByMessageId.TryGetValue(cacheableMessage.Id, out var activePoll) &&
                await channel.GetMessageAsync(cacheableMessage.Id).ConfigureAwait(false) is IUserMessage pollMessage &&
                reaction.UserId != bot.Client.CurrentUser.Id &&
                reaction.User.IsSpecified &&
                reaction.User.Value is SocketGuildUser reactingUser)
            {
                var (authorId, _, _, _, _, options, roleIds, allowedVotes, isSecretBallot, _, _) = await dataStore.GetPollAsync(activePoll.pollId).ConfigureAwait(false);
                var emote = reaction.Emote;
                if (options.FirstOrDefault(option => option.emoteName == reaction.Emote.Name) is { } selectedPollOption &&
                    !string.IsNullOrWhiteSpace(selectedPollOption.emoteName))
                {
                    if (roleIds.Count == 0 || roleIds.Intersect(reactingUser.Roles.Select(role => role.Id)).Any())
                    {
                        if (isSecretBallot)
                        {
                            await pollMessage.RemoveReactionAsync(reaction.Emote, reactingUser).ConfigureAwait(false);
                            var votes = await dataStore.GetPollVotesForUserAsync(activePoll.pollId, reactingUser.Id).ConfigureAwait(false);
                            if (votes.Contains(selectedPollOption.id))
                            {
                                await dataStore.RetractPollVoteAsync(reactingUser.Id, selectedPollOption.id).ConfigureAwait(false);
                                await reactingUser.SendMessageAsync($"Your vote for **{selectedPollOption.name}** has been retracted. To go back to the poll, click here: {pollMessage.GetJumpUrl()}").ConfigureAwait(false);
                            }
                            else if (allowedVotes != 0 && votes.Count >= allowedVotes)
                                await reactingUser.SendMessageAsync($"You have already reached the maximum of **{votes.Count}** vote{(votes.Count == 1 ? string.Empty : "s")}. You must retract one of your previous votes in order to cast a new one. To do that, react in the same way you did to cast the vote you wish to retract. To go back to the poll, click here: {pollMessage.GetJumpUrl()}").ConfigureAwait(false);
                            else
                            {
                                await dataStore.CastPollVoteAsync(reactingUser.Id, selectedPollOption.id).ConfigureAwait(false);
                                await reactingUser.SendMessageAsync($"Your vote for **{selectedPollOption.name}** has been counted. To go back to the poll, click here: {pollMessage.GetJumpUrl()}").ConfigureAwait(false);
                            }
                        }
                        else if (guildChannel is not null && allowedVotes != 0)
                        {
                            var existingVotesForUser = 0;
                            foreach (var kv in pollMessage.Reactions)
                            {
                                var reactionEmote = kv.Key;
                                if (options.FirstOrDefault(option => option.emoteName == reactionEmote.Name) is { } emoteOption &&
                                    !string.IsNullOrWhiteSpace(emoteOption.emoteName) &&
                                    (await AsyncEnumerableExtensions.FlattenAsync(pollMessage.GetReactionUsersAsync(reactionEmote, int.MaxValue)).ConfigureAwait(false)).Any(u => u.Id == reactingUser.Id))
                                    ++existingVotesForUser;
                            }
                            if (existingVotesForUser > allowedVotes)
                            {
                                await pollMessage.RemoveReactionAsync(reaction.Emote, reactingUser).ConfigureAwait(false);
                                await reactingUser.SendMessageAsync($"You have already reached the maximum of **{allowedVotes}** vote{(allowedVotes == 1 ? string.Empty : "s")}. You must clear one or more of your previous reactions to the poll in order to make a new one which will cast a different vote. To go back to the poll, click here: {pollMessage.GetJumpUrl()}").ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        await reactingUser.SendMessageAsync($"You're not allowed to vote in this poll.").ConfigureAwait(false);
                        await pollMessage.RemoveReactionAsync(reaction.Emote, reactingUser).ConfigureAwait(false);
                    }
                }
                else if (emote.Name == "✅" && reactingUser.Id == authorId)
                {
                    await dataStore.ClosePollAsync(activePoll.pollId).ConfigureAwait(false);
                    await ClosePollAsync(pollMessage).ConfigureAwait(false);
                }
                else
                    await pollMessage.RemoveReactionAsync(reaction.Emote, reactingUser).ConfigureAwait(false);
            }
        }

        async Task ClosePollAsync(IUserMessage message)
        {
            if (message.Channel is SocketGuildChannel guildChannel && activePollIdByMessageId.TryGetValue(message.Id, out var activePoll))
            {
                var (_, _, _, _, _, options, roleIds, allowedVotes, isSecretBallot, _, _) = await dataStore.GetPollAsync(activePoll.pollId).ConfigureAwait(false);
                var votesByUserId = new Dictionary<ulong, HashSet<int>>();
                if (!isSecretBallot)
                {
                    foreach (var kv in message.Reactions)
                    {
                        var emote = kv.Key;
                        if (options.FirstOrDefault(option => option.emoteName == emote.Name) is { } emoteOption &&
                            !string.IsNullOrWhiteSpace(emoteOption.emoteName))
                        {
                            foreach (var user in await AsyncEnumerableExtensions.FlattenAsync(message.GetReactionUsersAsync(emote, int.MaxValue)).ConfigureAwait(false))
                            {
                                if (!user.IsBot)
                                {
                                    var guildUser = guildChannel.Guild.GetUser(user.Id);
                                    if (roleIds.Count == 0 || roleIds.Intersect(guildUser.Roles.Select(role => role.Id)).Any())
                                    {
                                        if (!votesByUserId.TryGetValue(guildUser.Id, out var userOptionIds))
                                        {
                                            userOptionIds = new HashSet<int>();
                                            votesByUserId.Add(guildUser.Id, userOptionIds);
                                        }
                                        userOptionIds.Add(emoteOption.id);
                                    }
                                }
                            }
                        }
                    }
                    if (allowedVotes > 0)
                        foreach (var userId in votesByUserId.Keys)
                            votesByUserId[userId] = votesByUserId[userId].Take(allowedVotes).ToHashSet();
                    await dataStore.RemoveAllPollResultsAsync(activePoll.pollId).ConfigureAwait(false);
                    foreach (var kv in votesByUserId)
                    {
                        var userId = kv.Key;
                        var optionIds = kv.Value;
                        foreach (var optionId in optionIds)
                            await dataStore.CastPollVoteAsync(userId, optionId).ConfigureAwait(false);
                    }
                }
                await message.DeleteAsync().ConfigureAwait(false);
            }
        }

        async Task ClientReactionRemoved(Cacheable<IUserMessage, ulong> cacheableMessage, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.UserId == bot.Client.CurrentUser.Id && (pollsUnderConstructionByMessageId.ContainsKey(cacheableMessage.Id) || activePollIdByMessageId.ContainsKey(cacheableMessage.Id)))
                await (await cacheableMessage.GetOrDownloadAsync().ConfigureAwait(false)).AddReactionAsync(reaction.Emote).ConfigureAwait(false);
        }

        async Task ClientReactionsCleared(Cacheable<IUserMessage, ulong> cacheableMessage, ISocketMessageChannel channel)
        {
            if (pollsUnderConstructionByMessageId.ContainsKey(cacheableMessage.Id))
                await (await cacheableMessage.GetOrDownloadAsync().ConfigureAwait(false)).AddReactionsAsync
                (
                    new IEmote[]
                    {
                        new Emoji("❓"),
                        new Emoji("✅"),
                        new Emoji("#️⃣"),
                        new Emoji("🧑"),
                        new Emoji("🔒"),
                        new Emoji("▶"),
                        new Emoji("↔"),
                        new Emoji("⏹"),
                        new Emoji("💾"),
                        new Emoji("❌")
                    }
                ).ConfigureAwait(false);
            else if (activePollIdByMessageId.TryGetValue(cacheableMessage.Id, out var activePoll))
                await DisplayPollAsync(activePoll.pollId).ConfigureAwait(false);
        }

        async Task ClientReactionsRemovedForEmote(Cacheable<IUserMessage, ulong> cacheableMessage, ISocketMessageChannel channel, IEmote emote)
        {
            if (pollsUnderConstructionByMessageId.ContainsKey(cacheableMessage.Id))
                await(await cacheableMessage.GetOrDownloadAsync().ConfigureAwait(false)).AddReactionsAsync
                (
                    new IEmote[]
                    {
                        new Emoji("❓"),
                        new Emoji("✅"),
                        new Emoji("#️⃣"),
                        new Emoji("🧑"),
                        new Emoji("🔒"),
                        new Emoji("▶"),
                        new Emoji("↔"),
                        new Emoji("⏹"),
                        new Emoji("💾"),
                        new Emoji("❌")
                    }
                ).ConfigureAwait(false);
            else if (activePollIdByMessageId.TryGetValue(cacheableMessage.Id, out var activePoll))
                await DisplayPollAsync(activePoll.pollId).ConfigureAwait(false);
        }

        async Task<(ulong messageId, DateTimeOffset? end)> DisplayPollAsync(int pollId)
        {
            var (authorId, guildId, channelId, messageId, question, options, roleIds, allowedVotes, isSecretBallot, start, end) = await dataStore.GetPollAsync(pollId).ConfigureAwait(false);
            var channel = bot.Client.GetGuild(guildId).GetTextChannel(channelId);
            var message = messageId is { } nonNullMessageId ? (await channel.GetMessageAsync(nonNullMessageId).ConfigureAwait(false) is IUserMessage userMessage ? userMessage : null) : null;
            var embedFields = new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder
                {
                    Name = "Author",
                    Value = $"This question was posed by <@!{authorId}>.",
                    IsInline = true
                }
            };
            if (roleIds.Any())
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Role Restriction",
                    Value = $"Only users that are a member of at least one of these roles may vote: {string.Join(", ", roleIds.Select(roleId => $"<@&{roleId}>"))}",
                    IsInline = true
                });
            if (allowedVotes == 0)
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Unlimited Votes",
                    Value = "Those voting may vote once for as many options as they wish.",
                    IsInline = true
                });
            else if (allowedVotes > 1)
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Multiple Votes",
                    Value = $"Those voting may vote for up to {allowedVotes:n0} options.",
                    IsInline = true
                });
            if (isSecretBallot)
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Secret Ballot",
                    Value = "Reactions casting votes will be counted, but cleared. Voters will receive a direct message in confirmation that their vote was counted. The names of voters will not be correlated to options in the final results.",
                    IsInline = true
                });
            else
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Public Ballot",
                    Value = "Reactions casting votes will be counted, but not cleared. The names of voters will be correlated to options in the final results.",
                    IsInline = true
                });
            if (end is { } nonNullEnd)
                embedFields.Add(new EmbedFieldBuilder
                {
                    Name = "Deadline",
                    Value = $"The deadline to vote is {nonNullEnd}.",
                    IsInline = true
                });
            var embedBuilder = new EmbedBuilder
            {
                Author = Bot.GetEmbedAuthorBuilder(),
                Title = question,
                Description = $"React to this message to cast a vote for one of the following options:\n\n{string.Join("\n", options.Select(option => $"{option.emoteName} {option.name}"))}",
                Timestamp = start,
                Fields = embedFields
            };
            if (message is null)
            {
                message = await channel.SendMessageAsync($"I request the feedback of {(roleIds.Count == 0 ? "anyone seeing this" : string.Join(", ", roleIds.Select(roleId => $"<@&{roleId}>")))} for the following poll.", embed: embedBuilder.Build()).ConfigureAwait(false);
                await dataStore.SetPollMessageAsync(pollId, message.Id).ConfigureAwait(false);
            }
            else
                await message.ModifyAsync(props =>
                {
                    props.Content = $"I request the feedback of {(roleIds.Count == 0 ? "anyone seeing this" : string.Join(", ", roleIds.Select(roleId => $"<@&{roleId}>")))} for the following poll.";
                    props.Embed = embedBuilder.Build();
                }).ConfigureAwait(false);
            await message.AddReactionsAsync(options.Select(option => new Emoji(option.emoteName)).Concat(new IEmote[]
            {
                new Emoji("✅")
            }).ToArray()).ConfigureAwait(false);
            await message.PinAsync().ConfigureAwait(false);
            return (message.Id, end);
        }

        protected override bool Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                var client = bot.Client;
                client.GuildAvailable -= ClientGuildAvailable;
                client.MessageDeleted -= ClientMessageDeleted;
                client.MessageReceived -= ClientMessageReceived;
                client.ReactionAdded -= ClientReactionAdded;
                client.ReactionRemoved -= ClientReactionRemoved;
                client.ReactionsCleared -= ClientReactionsCleared;
                client.ReactionsRemovedForEmote -= ClientReactionsRemovedForEmote;
            }
            return true;
        }

        public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
        {
            if (commandArgs.Count >= 1 && RequestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase) && message.Author is SocketGuildUser guildAuthor && guildAuthor.Guild is SocketGuild guild)
            {
                var isAdmin = bot.IsAdministrativeUser(message.Author);
                var isModerator = (await dataStore.GetPollingRolesAsync(message.Channel.Id).ConfigureAwait(false)).Any(roleId => guildAuthor.Roles.Any(role => role.Id == roleId));
                var secondArg = commandArgs[1];
                if (secondArg.Equals("ROLE", StringComparison.OrdinalIgnoreCase) && isAdmin && commandArgs.Count == 4)
                {
                    var thirdArg = commandArgs[2];
                    var roleName = commandArgs[3];
                    var role = guild.Roles.FirstOrDefault(role => role.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                    if (role is null)
                    {
                        await message.Channel.SendMessageAsync("Your request cannot be processed. That role was not found.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        return true;
                    }
                    else
                    {
                        if (thirdArg.Equals("ADD", StringComparison.OrdinalIgnoreCase))
                        {
                            await dataStore.AddPollingRoleAsync(message.Channel.Id, role.Id).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("This role is now equipped for Polling in this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            return true;
                        }
                        else if (thirdArg.Equals("REMOVE", StringComparison.OrdinalIgnoreCase))
                        {
                            await dataStore.RemovePollingRoleAsync(message.Channel.Id, role.Id).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("This role is no longer operational for Polling in this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            return true;
                        }
                    }
                }
                else if (isAdmin || isModerator)
                {
                    if (secondArg.Equals("ADD", StringComparison.OrdinalIgnoreCase) && commandArgs.Count == 2)
                    {
                        var channel = message.Channel;
                        var author = message.Author;
                        await message.DeleteAsync().ConfigureAwait(false);
                        if (pollsUnderConstructionByMessageId.ContainsKey(author.Id))
                            await channel.SendMessageAsync("Your request cannot be processed. You already have a poll under construction.").ConfigureAwait(false);
                        else
                        {
                            var pollBuilder = new PollBuilder(author.Id);
                            var sentMessage = await channel.SendMessageAsync("Please wait...").ConfigureAwait(false);
                            await sentMessage.AddReactionsAsync
                            (
                                new IEmote[]
                                {
                                    new Emoji("❓"),
                                    new Emoji("✅"),
                                    new Emoji("#️⃣"),
                                    new Emoji("🧑"),
                                    new Emoji("🔒"),
                                    new Emoji("▶"),
                                    new Emoji("↔"),
                                    new Emoji("⏹"),
                                    new Emoji("💾"),
                                    new Emoji("❌")
                                }
                            ).ConfigureAwait(false);
                            pollsUnderConstructionByMessageId.AddOrUpdate(sentMessage.Id, pollBuilder, (k, v) => pollBuilder);
                            await RenderPollBuilderMessageAsync(sentMessage, pollBuilder).ConfigureAwait(false);
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        static async Task RenderPollBuilderMessageAsync(IUserMessage pollBuilderMessage, PollBuilder pollBuilder)
        {
            await pollBuilderMessage.ModifyAsync(props =>
            {
                var embedFieldBuilders = new List<EmbedFieldBuilder>();
                if (!string.IsNullOrWhiteSpace(pollBuilder.Question))
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = "Question",
                        Value = pollBuilder.Question,
                        IsInline = false
                    });
                var optionNumber = 0;
                foreach (var option in pollBuilder.Options)
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"Option {++optionNumber}",
                        Value = option,
                        IsInline = false
                    });
                embedFieldBuilders.Add(new EmbedFieldBuilder
                {
                    Name = $"Start",
                    Value = pollBuilder.Start.ToString(),
                    IsInline = true
                });
                if (pollBuilder.End is { } end)
                {
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"Duration",
                        Value = pollBuilder.Duration.ToString(),
                        IsInline = true
                    });
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"End",
                        Value = end.ToString(),
                        IsInline = true
                    });
                }
                if (pollBuilder.AllowedVotes != 1)
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"Allowed Votes per User",
                        Value = pollBuilder.AllowedVotes == 0 ? "Up to All Options" : pollBuilder.AllowedVotes.ToString(),
                        IsInline = true
                    });
                if (pollBuilder.RoleIds.Any() && pollBuilderMessage.Channel is SocketGuildChannel guildChannel)
                {
                    var guildRoles = guildChannel.Guild.Roles.ToDictionary(role => role.Id);
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"Allowed Roles",
                        Value = string.Join(", ", pollBuilder.RoleIds.Select(roleId => guildRoles[roleId].Name).OrderBy(str => str)),
                        IsInline = true
                    });
                }
                if (pollBuilder.IsSecretBallot)
                    embedFieldBuilders.Add(new EmbedFieldBuilder
                    {
                        Name = $"Ballot Type",
                        Value = "Secret",
                        IsInline = true
                    });
                props.Content = $@"{pollBuilder.State switch
                {
                    PollBuilderState.AllowedVotes => "**Reply to this message with the number of votes allowed per voter, or `0` to specify that voters may vote for every option.**",
                    PollBuilderState.Duration => "**Reply to this message with the amount of time to allow voting (format: [d].h:mm:ss) or `infinite`.**",
                    PollBuilderState.End => "**Reply to this message with the time voting will end or `none`.**",
                    PollBuilderState.Options => "**Reply to this message with the options for the poll separated by spaces. To include spaces in an option, wrap it in `\"`. To include double quotes in a quoted option, repeat them once.**",
                    PollBuilderState.Question => "**Reply to this message with the question asked by the poll.**",
                    PollBuilderState.Roles => "**Reply to this message with the names of the Discord roles that may vote separated by spaces. To include spaces in the name of a role, wrap it in `\"`. To include double quotes in a quoted name of a role, repeat them once.**",
                    PollBuilderState.Start => "**Reply to this message with the time voting will start or `now`.**",
                    _ => "**React to this message to change the poll building mode.** React with 💾 to save the poll."
                }}  React with ❌ to cancel.";
                props.Embed = new EmbedBuilder
                {
                    Author = Bot.GetEmbedAuthorBuilder(),
                    Title = "Poll Details",
                    Fields = embedFieldBuilders
                }.Build();
            }).ConfigureAwait(false);
        }

        async void TimerTick(object state)
        {
            try
            {
                var backInTheBag = new List<(int pollId, DateTimeOffset start)>();
                while (pollsAwaitingDisplay.TryTake(out var pollAwaitingDisplay))
                {
                    if (pollAwaitingDisplay.start <= DateTimeOffset.UtcNow)
                    {
                        var pollId = pollAwaitingDisplay.pollId;
                        var (messageId, end) = await DisplayPollAsync(pollId).ConfigureAwait(false);
                        activePollIdByMessageId.AddOrUpdate(messageId, (pollId, end), (k, v) => (pollId, end));
                    }
                    else
                        backInTheBag.Add(pollAwaitingDisplay);
                }
                foreach (var undisplayedPollAwaitingDisplay in backInTheBag)
                    pollsAwaitingDisplay.Add(undisplayedPollAwaitingDisplay);
                foreach (var kv in activePollIdByMessageId)
                {
                    var end = kv.Value.end;
                    if (end is not null && DateTimeOffset.UtcNow >= end)
                    {
                        var (_, guildId, channelId, _, _, _, _, _, _, _, _) = await dataStore.GetPollAsync(kv.Value.pollId).ConfigureAwait(false);
                        if (await bot.Client.GetGuild(guildId).GetTextChannel(channelId).GetMessageAsync(kv.Key).ConfigureAwait(false) is IUserMessage message)
                            await ClosePollAsync(message).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            }
        }

        static readonly string[] optionEmoteChars = new string[]
        {
            "\U0001F1E6", // regional_indicator_a
            "\U0001F1E7", // regional_indicator_b
            "\U0001F1E8", // regional_indicator_c
            "\U0001F1E9", // regional_indicator_d
            "\U0001F1EA", // regional_indicator_e
            "\U0001F1EB", // regional_indicator_f
            "\U0001F1EC", // regional_indicator_g
            "\U0001F1ED", // regional_indicator_h
            "\U0001F1EE", // regional_indicator_i
            "\U0001F1EF", // regional_indicator_j
            "\U0001F1F0", // regional_indicator_k
            "\U0001F1F1", // regional_indicator_l
            "\U0001F1F2", // regional_indicator_m
            "\U0001F1F3", // regional_indicator_n
            "\U0001F1F4", // regional_indicator_o
            "\U0001F1F5", // regional_indicator_p
            "\U0001F1F6", // regional_indicator_q
            "\U0001F1F7", // regional_indicator_r
            "\U0001F1F8", // regional_indicator_s
            "\U0001F1F9", // regional_indicator_t
            "\U0001F1FA", // regional_indicator_u
            "\U0001F1FB", // regional_indicator_v
            "\U0001F1FC", // regional_indicator_w
            "\U0001F1FD", // regional_indicator_x
            "\U0001F1FE", // regional_indicator_y
            "\U0001F1FF" // regional_indicator_z
        };
    }
}
