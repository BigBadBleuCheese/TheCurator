using Cogs.Disposal;
using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TheCurator.Logic.Data;

namespace TheCurator.Logic.Features
{
    public class SuicideKings : SyncDisposable, IFeature
    {
        public SuicideKings(IDataStore dataStore, IBot bot)
        {
            this.dataStore = dataStore;
            this.bot = bot;
            this.bot.Client.ReactionAdded += ClientReactionAdded;
            activeEvents = new ConcurrentDictionary<ulong, IReadOnlyList<int>>();
        }

        readonly ConcurrentDictionary<ulong, IReadOnlyList<int>> activeEvents;
        readonly IBot bot;
        readonly IDataStore dataStore;

        Task ClientReactionAdded(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            return Task.CompletedTask;
        }

        protected override bool Dispose(bool disposing)
        {
            if (disposing)
                bot.Client.ReactionAdded -= ClientReactionAdded;
            return true;
        }

        async Task<IReadOnlyList<string>?> GetEventSublistAsync(ISocketMessageChannel channel, int listId) =>
            activeEvents.TryGetValue(channel.Id, out var memberIds)
            ?
            (await dataStore.GetSuicideKingsListMembersInPositionOrderAsync(listId).ConfigureAwait(false))
                .Where(e => memberIds.Contains(e.memberId))
                .Select(e => e.name)
                .ToImmutableArray()
            :
            null;

        async Task<IReadOnlyList<string>?> GetMasterListAsync(ISocketMessageChannel channel, int listId) =>
            (await dataStore.GetSuicideKingsListMembersInPositionOrderAsync(listId).ConfigureAwait(false))
                .Select(e => e.name)
                .ToImmutableArray();

        async Task PrintEventSublistsAsync(ISocketMessageChannel channel, MessageReference? messageReference = null)
        {
            var fieldEmbeds = new List<EmbedFieldBuilder>();
            foreach (var (listId, listName) in (await dataStore.GetSuicideKingsListsAsync(channel.Id).ConfigureAwait(false)).OrderBy(l => l.name))
                fieldEmbeds.Add(new EmbedFieldBuilder
                {
                    Name = listName,
                    Value = string.Join("\n", await GetEventSublistAsync(channel, listId).ConfigureAwait(false)),
                    IsInline = true
                });
            await channel.SendMessageAsync(embed: new EmbedBuilder
            {
                Title = $"Current State of Sub Lists",
                Fields = fieldEmbeds,
                Timestamp = DateTimeOffset.UtcNow
            }.Build(), messageReference: messageReference).ConfigureAwait(false);
        }

        async Task PrintMasterListsAsync(ISocketMessageChannel channel, MessageReference? messageReference = null)
        {
            var fieldEmbeds = new List<EmbedFieldBuilder>();
            foreach (var (listId, listName) in (await dataStore.GetSuicideKingsListsAsync(channel.Id).ConfigureAwait(false)).OrderBy(l => l.name))
                fieldEmbeds.Add(new EmbedFieldBuilder
                {
                    Name = listName,
                    Value = string.Join("\n", await GetMasterListAsync(channel, listId).ConfigureAwait(false)),
                    IsInline = true
                });
            await channel.SendMessageAsync(embed: new EmbedBuilder
            {
                Title = $"Current State of Master Lists",
                Fields = fieldEmbeds,
                Timestamp = DateTimeOffset.UtcNow
            }.Build(), messageReference: messageReference).ConfigureAwait(false);
        }

        public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
        {
            if (commandArgs.Count >= 1 && requestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase) && message.Author is SocketGuildUser guildAuthor && guildAuthor.Guild is SocketGuild guild)
            {
                var isAdmin = bot.IsAdministrativeUser(message.Author);
                var isModerator = (await dataStore.GetSuicideKingsRolesAsync(guildAuthor.Guild.Id).ConfigureAwait(false)).Any(roleId => guildAuthor.Roles.Any(role => role.Id == roleId));
                var secondArg = commandArgs[1];
                if (secondArg.Equals("PRINT", StringComparison.OrdinalIgnoreCase))
                {
                    if (activeEvents.ContainsKey(message.Channel.Id))
                        await PrintEventSublistsAsync(message.Channel, new MessageReference(message.Id)).ConfigureAwait(false);
                    else
                        await PrintMasterListsAsync(message.Channel, new MessageReference(message.Id)).ConfigureAwait(false);
                }
                else if (secondArg.Equals("ROLE", StringComparison.OrdinalIgnoreCase) && isAdmin && commandArgs.Count == 4)
                {
                    var thirdArg = commandArgs[2];
                    var roleName = commandArgs[3];
                    var role = guild.Roles.FirstOrDefault(role => role.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                    if (role is null)
                        await message.Channel.SendMessageAsync("Your request cannot be processed. That role was not found.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    else
                    {
                        if (thirdArg.Equals("ADD", StringComparison.OrdinalIgnoreCase))
                        {
                            await dataStore.AddSuicideKingsRoleAsync(guild.Id, role.Id).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("This role is now equipped for Suicide Kings.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        }
                        else if (thirdArg.Equals("REMOVE", StringComparison.OrdinalIgnoreCase))
                        {
                            await dataStore.RemoveSuicideKingsRoleAsync(guild.Id, role.Id).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("This role is no longer operational for Suicide Kings.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        }
                        else
                            await message.Channel.SendMessageAsync("Your request cannot be processed.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                }
                else if (isAdmin || isModerator)
                {
                    if (secondArg.Equals("LIST", StringComparison.OrdinalIgnoreCase) && commandArgs.Count == 4)
                    {
                        var thirdArg = commandArgs[2];
                        var name = commandArgs[3];
                        if (thirdArg.Equals("ADD", StringComparison.OrdinalIgnoreCase))
                        {
                            if (await dataStore.GetSuicideKingsListIdByNameAsync(message.Channel.Id, name).ConfigureAwait(false) is not null)
                                await message.Channel.SendMessageAsync($"Your request cannot be processed. Suicide Kings list named `{name}` already associated with this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            else
                            {
                                var listId = await dataStore.AddSuicideKingsListAsync(message.Channel.Id, name).ConfigureAwait(false);
                                var membersToAdd = (await dataStore.GetSuicideKingsMembersAsync(message.Channel.Id).ConfigureAwait(false)).ToList();
                                var rnd = new Random();
                                var position = 0;
                                while (membersToAdd.Any())
                                {
                                    var memberIndex = rnd.Next(membersToAdd.Count);
                                    var (memberId, _) = membersToAdd[memberIndex];
                                    membersToAdd.RemoveAt(memberIndex);
                                    await dataStore.SetSuicideKingsListMemberEntryAsync(listId, memberId, ++position).ConfigureAwait(false);
                                }
                                await message.Channel.SendMessageAsync($"This channel is now equipped with Suicide Kings list `{name}`.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            }
                        }
                        else if (thirdArg.Equals("REMOVE", StringComparison.OrdinalIgnoreCase))
                        {
                            var listId = await dataStore.GetSuicideKingsListIdByNameAsync(message.Channel.Id, name).ConfigureAwait(false);
                            if (listId is null)
                                await message.Channel.SendMessageAsync($"Your request cannot be processed. Could not find Suicide Kings list named `{name}` associated with this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            else
                            {
                                await dataStore.RemoveSuicideKingsListAsync(listId.Value).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync($"The Suicide Kings list `{name}` associated with this channel is no longer operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            }
                        }
                        else
                            return false;
                    }
                    else if (secondArg.Equals("MEMBER", StringComparison.OrdinalIgnoreCase) && commandArgs.Count >= 4)
                    {
                        var thirdArg = commandArgs[2];
                        if (thirdArg.Equals("ADD", StringComparison.OrdinalIgnoreCase))
                        {
                            var names = commandArgs.Skip(3).ToImmutableArray();
                            var nameFound = false;
                            foreach (var name in names)
                                if (await dataStore.GetSuicideKingsMemberIdByNameAsync(message.Channel.Id, name).ConfigureAwait(false) is not null)
                                {
                                    nameFound = true;
                                    await message.Channel.SendMessageAsync($"Your request cannot be processed. Suicide Kings member named `{name}` already associated with this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                }
                            if (!nameFound)
                            {
                                var membersAdded = new List<int>();
                                foreach (var name in names)
                                    membersAdded.Add(await dataStore.AddSuicideKingsMemberAsync(message.Channel.Id, name).ConfigureAwait(false));
                                var rnd = new Random();
                                foreach (var (listId, _) in await dataStore.GetSuicideKingsListsAsync(message.Channel.Id).ConfigureAwait(false))
                                {
                                    var currentPositions = await dataStore.GetSuicideKingsListMembersInPositionOrderAsync(listId).ConfigureAwait(false);
                                    var position = 0;
                                    foreach (var (existingMemberId, _) in currentPositions)
                                        await dataStore.SetSuicideKingsListMemberEntryAsync(listId, existingMemberId, ++position).ConfigureAwait(false);
                                    var membersToAdd = membersAdded.ToList();
                                    while (membersToAdd.Any())
                                    {
                                        var memberIndex = rnd.Next(membersToAdd.Count);
                                        var memberId = membersToAdd[memberIndex];
                                        membersToAdd.RemoveAt(memberIndex);
                                        await dataStore.SetSuicideKingsListMemberEntryAsync(listId, memberId, ++position).ConfigureAwait(false);
                                    }
                                }
                                await message.Channel.SendMessageAsync($"This channel is now equipped with Suicide Kings members {string.Join(", ", names.Select(name => $"`{name}`"))}.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            }
                        }
                        else if (thirdArg.Equals("RETIRE", StringComparison.OrdinalIgnoreCase) && commandArgs.Count == 4)
                        {
                            var name = commandArgs[3];
                            var memberId = await dataStore.GetSuicideKingsMemberIdByNameAsync(message.Channel.Id, name).ConfigureAwait(false);
                            if (memberId is null)
                                await message.Channel.SendMessageAsync($"Your request cannot be processed. Could not find Suicide Kings member named `{name}` associated with this channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            else
                            {
                                await dataStore.RetireSuicideKingsMemberAsync(memberId.Value).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync($"The Suicide Kings member `{name}` associated with this channel is no longer operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            }
                        }
                        else
                            return false;
                    }
                    else if (secondArg.Equals("EVENT", StringComparison.OrdinalIgnoreCase) && commandArgs.Count >= 3)
                    {
                        var thirdArg = commandArgs[2];
                        if (thirdArg.Equals("START", StringComparison.OrdinalIgnoreCase))
                        {
                            if (commandArgs.Count == 3)
                                await message.Channel.SendMessageAsync("Your request cannot be processed. No members were listed for the event.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            else if (activeEvents.ContainsKey(message.Channel.Id))
                                await message.Channel.SendMessageAsync("Your request cannot be processed. This channel already has an active event.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            else
                            {
                                string? memberNameNotFound = null;
                                var memberIds = new List<int>();
                                var names = commandArgs.Skip(3).ToImmutableArray();
                                foreach (var name in names)
                                {
                                    if (await dataStore.GetSuicideKingsMemberIdByNameAsync(message.Channel.Id, name).ConfigureAwait(false) is { } memberId)
                                        memberIds.Add(memberId);
                                    else
                                    {
                                        memberNameNotFound = name;
                                        break;
                                    }
                                }
                                if (memberNameNotFound is not null)
                                    await message.Channel.SendMessageAsync($"Your request cannot be processed. Could not find member `{memberNameNotFound}`.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                else
                                {
                                    activeEvents.AddOrUpdate(message.Channel.Id, memberIds.ToImmutableArray(), (k, v) => memberIds.ToImmutableArray());
                                    await message.Channel.SendMessageAsync($"This channel is now equipped with a Suicide Kings event for members {string.Join(", ", names.Select(name => $"`{name}`"))}.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    await PrintEventSublistsAsync(message.Channel).ConfigureAwait(false);
                                }
                            }
                        }
                        else if (thirdArg.Equals("STOP", StringComparison.OrdinalIgnoreCase) && commandArgs.Count == 3 && activeEvents.TryRemove(message.Channel.Id, out _))
                        {
                            await message.Channel.SendMessageAsync($"The Suicide Kings event for this channel is no longer operational.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            await PrintMasterListsAsync(message.Channel).ConfigureAwait(false);
                        }
                        else
                            return false;
                    }
                    else if (secondArg.Equals("DROP", StringComparison.OrdinalIgnoreCase) && commandArgs.Count >= 4)
                    {
                        if (activeEvents.TryGetValue(message.Channel.Id, out var memberIds))
                        {
                            var memberName = commandArgs[2];
                            var listName = commandArgs[3];
                            var reason = commandArgs.Count >= 5 ? string.Join(" ", commandArgs.Skip(4)) : null;
                            var memberId = await dataStore.GetSuicideKingsMemberIdByNameAsync(message.Channel.Id, memberName).ConfigureAwait(false);
                            if (memberId is { } nonNullMemberId)
                            {
                                if (!memberIds.Contains(nonNullMemberId))
                                    await message.Channel.SendMessageAsync($"Your request cannot be processed. The member `{memberName}` is not a part of the active event.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                else
                                {
                                    var listId = await dataStore.GetSuicideKingsListIdByNameAsync(message.Channel.Id, listName).ConfigureAwait(false);
                                    if (listId is { } nonNullListId)
                                    {
                                        var dropId = await dataStore.AddSuicideKingsDropAsync(nonNullListId, nonNullMemberId, DateTimeOffset.UtcNow, reason).ConfigureAwait(false);
                                        foreach (var otherMemberId in memberIds.Except(new int[] { nonNullListId }))
                                            await dataStore.AddSuicideKingsDropWitnessAsync(dropId, otherMemberId).ConfigureAwait(false);
                                        var masterList = (await dataStore.GetSuicideKingsListMembersInPositionOrderAsync(nonNullListId).ConfigureAwait(false)).Select(e => e.memberId).ToList();
                                        var subList = masterList.Where(i => memberIds.Contains(i)).ToList();
                                        var subListPositionsInMasterList = new List<int>();
                                        for (var i = masterList.Count - 1; i >= 0; --i)
                                            if (memberIds.Contains(masterList[i]))
                                            {
                                                masterList.RemoveAt(i);
                                                subListPositionsInMasterList.Insert(0, i);
                                            }
                                        subList.Remove(nonNullMemberId);
                                        subList.Add(nonNullMemberId);
                                        foreach (var subListPositionInMasterList in subListPositionsInMasterList)
                                        {
                                            masterList.Insert(subListPositionInMasterList, subList[0]);
                                            subList.RemoveAt(0);
                                        }
                                        var position = 0;
                                        foreach (var memberIdInMasterList in masterList)
                                            await dataStore.SetSuicideKingsListMemberEntryAsync(nonNullListId, memberIdInMasterList, ++position).ConfigureAwait(false);
                                        var fieldEmbeds = new List<EmbedFieldBuilder>
                                        {
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Member",
                                                Value = memberName,
                                                IsInline = true
                                            },
                                            new EmbedFieldBuilder
                                            {
                                                Name = "List",
                                                Value = listName,
                                                IsInline = true
                                            }
                                        };
                                        if (reason is not null)
                                            fieldEmbeds.Add(new EmbedFieldBuilder
                                            {
                                                Name = "Note",
                                                Value = reason,
                                                IsInline = true
                                            });
                                        await message.Channel.SendMessageAsync(embed: new EmbedBuilder
                                        {
                                            Title = $"Registered Drop",
                                            Fields = fieldEmbeds,
                                            Timestamp = DateTimeOffset.UtcNow
                                        }.Build(), messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                        await PrintEventSublistsAsync(message.Channel).ConfigureAwait(false);
                                    }
                                    else
                                        await message.Channel.SendMessageAsync($"Your request cannot be processed. The list `{listName}` was not found.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                }
                            }
                            else
                                await message.Channel.SendMessageAsync($"Your request cannot be processed. The member `{memberName}` was not found.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        }
                        else
                            await message.Channel.SendMessageAsync("Your request cannot be processed. This channel does not currently have an active event.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                }
                return true;
            }
            return false;
        }

        static readonly string[] requestIdentifiers = new string[] { "SK", "SUICIDEKINGS" };
    }
}
