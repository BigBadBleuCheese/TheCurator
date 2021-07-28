using Cogs.Disposal;
using SQLite;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data.SQLite
{
    public class DataStore : AsyncDisposable, IDataStore
    {
        public DataStore() => connection = new SQLiteAsyncConnection(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "data.sqlite"));

        readonly SQLiteAsyncConnection connection;

        public async Task ConnectAsync()
        {
            var schemaVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;").ConfigureAwait(false);
            var readSchemaVersion = schemaVersion;
            if (schemaVersion == 0)
            {
                await connection.CreateTableAsync<CountingChannel>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsList>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsListEntry>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsMember>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsRole>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsDrop>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsDropWitness>().ConfigureAwait(false);
                schemaVersion = 2;
            }
            if (schemaVersion == 1)
            {
                async Task<long> getSnowflakeAsync(long id) => ulong.Parse(await connection.ExecuteScalarAsync<string>("select DiscordId from Snowflake where Id = ?", id)).ToSigned();
                var updateObjects = new List<object>();
                await connection.ExecuteAsync("create table NewCountingChannel (ChannelId bigint not null primary key, Count integer, LastAuthorId bigint);");
                await connection.ExecuteAsync("insert into NewCountingChannel select ChannelId, Count, LastAuthorId from CountingChannel;");
                await connection.ExecuteAsync("drop table CountingChannel;");
                await connection.ExecuteAsync("alter table `NewCountingChannel` RENAME TO `CountingChannel`;");
                foreach (var countingChannel in await connection.Table<CountingChannel>().ToListAsync().ConfigureAwait(false))
                {
                    if (countingChannel.LastAuthorId == 0)
                        await connection.ExecuteAsync
                        (
                            "update CountingChannel set ChannelId = ? where ChannelId = ?",
                            await getSnowflakeAsync(countingChannel.ChannelId).ConfigureAwait(false),
                            countingChannel.ChannelId
                        ).ConfigureAwait(false);
                    else
                        await connection.ExecuteAsync
                        (
                            "update CountingChannel set ChannelId = ?, LastAuthorId = ? where ChannelId = ?",
                            await getSnowflakeAsync(countingChannel.ChannelId).ConfigureAwait(false),
                            await getSnowflakeAsync(countingChannel.LastAuthorId).ConfigureAwait(false),
                            countingChannel.ChannelId
                        ).ConfigureAwait(false);
                }
                await connection.ExecuteAsync("create table NewSuicideKingsList (ListId integer not null primary key autoincrement, ChannelId bigint not null, Name varchar not null);");
                await connection.ExecuteAsync("drop index UX_SuicideKingsList;");
                await connection.ExecuteAsync("create unique index UX_SuicideKingsList on NewSuicideKingsList (ChannelId, Name);");
                await connection.ExecuteAsync("insert into NewSuicideKingsList select ListId, ChannelId, Name from SuicideKingsList;");
                await connection.ExecuteAsync("drop table SuicideKingsList;");
                await connection.ExecuteAsync("alter table `NewSuicideKingsList` RENAME TO `SuicideKingsList`;");
                foreach (var suicideKingsList in await connection.Table<SuicideKingsList>().ToListAsync().ConfigureAwait(false))
                {
                    suicideKingsList.ChannelId = await getSnowflakeAsync(suicideKingsList.ChannelId).ConfigureAwait(false);
                    updateObjects.Add(suicideKingsList);
                }
                await connection.ExecuteAsync("create table NewSuicideKingsMember (MemberId integer not null primary key autoincrement, ChannelId bigint not null, Name varchar not null, Retired bigint);");
                await connection.ExecuteAsync("drop index UX_SuicideKingsMember;");
                await connection.ExecuteAsync("create unique index UX_SuicideKingsMember on NewSuicideKingsMember (ChannelId, Name);");
                await connection.ExecuteAsync("insert into NewSuicideKingsMember select MemberId, ChannelId, Name, Retired from SuicideKingsMember;");
                await connection.ExecuteAsync("drop table SuicideKingsMember;");
                await connection.ExecuteAsync("alter table `NewSuicideKingsMember` RENAME TO `SuicideKingsMember`;");
                foreach (var suicideKingsMember in await connection.Table<SuicideKingsMember>().ToListAsync().ConfigureAwait(false))
                {
                    suicideKingsMember.ChannelId = await getSnowflakeAsync(suicideKingsMember.ChannelId).ConfigureAwait(false);
                    updateObjects.Add(suicideKingsMember);
                }
                await connection.ExecuteAsync("create table NewSuicideKingsRole (GuildId bigint not null, RoleId bigint not null);");
                await connection.ExecuteAsync("drop index UX_SuicideKingsRole;");
                await connection.ExecuteAsync("create unique index UX_SuicideKingsRole on NewSuicideKingsRole (GuildId, RoleId);");
                await connection.ExecuteAsync("insert into NewSuicideKingsRole select GuildId, RoleId from SuicideKingsRole where GuildId is not null and RoleId is not null;");
                await connection.ExecuteAsync("drop table SuicideKingsRole;");
                await connection.ExecuteAsync("alter table `NewSuicideKingsRole` RENAME TO `SuicideKingsRole`;");
                foreach (var suicideKingsRole in await connection.Table<SuicideKingsRole>().ToListAsync().ConfigureAwait(false))
                    await connection.ExecuteAsync
                    (
                        "update SuicideKingsRole set GuildId = ?, RoleId = ? where GuildId = ? and RoleId = ?",
                        await getSnowflakeAsync(suicideKingsRole.GuildId).ConfigureAwait(false),
                        await getSnowflakeAsync(suicideKingsRole.RoleId).ConfigureAwait(false),
                        suicideKingsRole.GuildId,
                        suicideKingsRole.RoleId
                    ).ConfigureAwait(false);
                await connection.UpdateAllAsync(updateObjects).ConfigureAwait(false);
                await connection.ExecuteAsync("drop table Snowflake;");
                schemaVersion = 2;
            }
            if (schemaVersion == 2)
            {
                await connection.CreateTableAsync<Poll>().ConfigureAwait(false);
                await connection.CreateTableAsync<PollingRole>().ConfigureAwait(false);
                await connection.CreateTableAsync<PollOption>().ConfigureAwait(false);
                await connection.CreateTableAsync<PollRole>().ConfigureAwait(false);
                await connection.CreateTableAsync<PollVote>().ConfigureAwait(false);
                schemaVersion = 3;
            }
            if (schemaVersion != readSchemaVersion)
                await connection.ExecuteScalarAsync<int>($"PRAGMA user_version = {schemaVersion};").ConfigureAwait(false);
        }

        public Task DisconnectAsync() => connection.CloseAsync();

        protected override async ValueTask<bool> DisposeAsync(bool disposing)
        {
            if (disposing)
                await DisconnectAsync().ConfigureAwait(false);
            return true;
        }

        #region Counting

        public async Task<(int? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId)
        {
            var id = channelId.ToSigned();
            var countingChannel = await connection.Table<CountingChannel>().FirstOrDefaultAsync(cc => cc.ChannelId == id).ConfigureAwait(false);
            return countingChannel is null ? (null, null) : (countingChannel.Count, countingChannel.LastAuthorId.ToUnsigned());
        }

        public async Task SetCountingChannelCountAsync(ulong channelId, int? count, ulong? lastAuthorId)
        {
            if (count is { } nonNullCount && lastAuthorId is { } nonNullLastAuthorId)
                await connection.InsertOrReplaceAsync(new CountingChannel
                {
                    ChannelId = channelId.ToSigned(),
                    Count = nonNullCount,
                    LastAuthorId = nonNullLastAuthorId.ToSigned(),
                }).ConfigureAwait(false);
        }

        #endregion Counting

        #region Polling

        public async Task<int> AddPollAsync(ulong authorId, ulong guildId, ulong channelId, string question, IReadOnlyList<(string name, string emoteName)> options, IReadOnlyList<ulong> roleIds, int allowedVotes, bool isSecretBallot, DateTimeOffset start, DateTimeOffset? end)
        {
            var poll = new Poll
            {
                AllowedVotes = allowedVotes,
                AuthorId = authorId.ToSigned(),
                ChannelId = channelId.ToSigned(),
                End = end,
                GuildId = guildId.ToSigned(),
                IsSecretBallot = isSecretBallot,
                Question = question,
                Start = start
            };
            await connection.InsertAsync(poll).ConfigureAwait(false);
            var pollId = poll.PollId;
            var insertObjects = new List<object>();
            insertObjects.AddRange(options.Select((option, index) => new PollOption
            {
                EmoteName = option.emoteName,
                Name = option.name,
                Order = index + 1,
                PollId = pollId
            }));
            insertObjects.AddRange(roleIds.Select(roleId => new PollRole
            {
                PollId = pollId,
                RoleId = roleId.ToSigned()
            }));
            await connection.InsertAllAsync(insertObjects).ConfigureAwait(false);
            return pollId;
        }

        public async Task AddPollingRoleAsync(ulong channelId, ulong roleId) =>
            await connection.InsertAsync(new PollingRole
            {
                ChannelId = channelId.ToSigned(),
                RoleId = roleId.ToSigned()
            }).ConfigureAwait(false);

        public Task CastPollVoteAsync(ulong userId, int optionId) =>
            connection.InsertAsync(new PollVote
            {
                OptionId = optionId,
                UserId = userId.ToSigned()
            });

        public async Task ClosePollAsync(int pollId)
        {
            var poll = await connection.GetAsync<Poll>(pollId).ConfigureAwait(false);
            poll.End = DateTimeOffset.UtcNow;
            await connection.UpdateAsync(poll).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<(int pollId, DateTimeOffset start)>> GetOpenOrPendingPollsForGuildAsync(ulong guildId)
        {
            var now = DateTimeOffset.UtcNow;
            var gId = guildId.ToSigned();
            return (await connection.Table<Poll>().Where(poll => poll.GuildId == gId && (poll.End == null || poll.End > now)).ToListAsync().ConfigureAwait(false)).Select(poll => (poll.PollId, poll.Start)).ToImmutableArray();
        }

        public async Task<(ulong authorId, ulong guildId, ulong channelId, ulong? messageId, string question, IReadOnlyList<(int id, string name, string emoteName)> options, IReadOnlyList<ulong> roleIds, int allowedVotes, bool isSecretBallot, DateTimeOffset start, DateTimeOffset? end)> GetPollAsync(int pollId)
        {
            var poll = await connection.GetAsync<Poll>(pollId).ConfigureAwait(false);
            var options = await connection.Table<PollOption>().Where(option => option.PollId == pollId).OrderBy(option => option.Order).ToListAsync().ConfigureAwait(false);
            var roles = await connection.Table<PollRole>().Where(role => role.PollId == pollId).ToListAsync().ConfigureAwait(false);
            return
            (
                poll.AuthorId.ToUnsigned(),
                poll.GuildId.ToUnsigned(),
                poll.ChannelId.ToUnsigned(),
                poll.MessageId?.ToUnsigned(),
                poll.Question!,
                options.Select(option => (option.OptionId, option.Name, option.EmoteName)).ToImmutableArray(),
                roles.Select(role => role.RoleId.ToUnsigned()).ToImmutableArray(),
                poll.AllowedVotes,
                poll.IsSecretBallot,
                poll.Start,
                poll.End
            );
        }

        public async Task<IReadOnlyList<ulong>> GetPollingRolesAsync(ulong channelId)
        {
            var roles = new List<ulong>();
            var cId = channelId.ToSigned();
            foreach (var roleId in (await connection.Table<PollingRole>().Where(r => r.ChannelId == cId).ToListAsync().ConfigureAwait(false)).Select(r => r.RoleId))
                roles.Add(roleId.ToUnsigned());
            return roles;
        }

        public async Task<IReadOnlyDictionary<int, IReadOnlyList<ulong>>> GetPollResultsAsync(int pollId) =>
            (await connection.QueryAsync<PollVote>("select v.OptionId, v.UserId from PollVote v join PollOption o on o.OptionId = v.OptionId where o.PollId = ?", pollId).ConfigureAwait(false)).GroupBy(v => v.OptionId).ToImmutableDictionary(g => g.Key, g => (IReadOnlyList<ulong>)g.Select(v => v.UserId.ToUnsigned()).ToImmutableArray());

        public async Task<IReadOnlyList<int>> GetPollVotesForUserAsync(int pollId, ulong userId)
        {
            var optionIds = (await connection.Table<PollOption>().Where(option => option.PollId == pollId).ToListAsync().ConfigureAwait(false)).Select(option => option.OptionId).ToArray();
            var uId = userId.ToSigned();
            var votes = await connection.Table<PollVote>().Where(vote => optionIds.Contains(vote.OptionId) && vote.UserId == uId).ToListAsync().ConfigureAwait(false);
            return votes.Select(vote => vote.OptionId).ToImmutableArray();
        }

        public async Task RemoveAllPollResultsAsync(int pollId)
        {
            var optionIds = (await connection.Table<PollOption>().Where(option => option.PollId == pollId).ToListAsync().ConfigureAwait(false)).Select(option => option.OptionId).ToImmutableArray();
            await connection.ExecuteAsync($"delete from PollVote where OptionId in ({string.Join(", ", optionIds)})").ConfigureAwait(false);
        }

        public async Task RemovePollingRoleAsync(ulong channelId, ulong roleId)
        {
            var cId = channelId.ToSigned();
            var rId = roleId.ToSigned();
            await connection.Table<PollingRole>().DeleteAsync(r => r.ChannelId == cId && r.RoleId == rId).ConfigureAwait(false);
        }

        public Task RetractPollVoteAsync(ulong userId, int optionId)
        {
            var uId = userId.ToSigned();
            return connection.Table<PollVote>().Where(vote => vote.UserId == uId && vote.OptionId == optionId).DeleteAsync();
        }

        public async Task SetPollMessageAsync(int pollId, ulong messageId)
        {
            var poll = await connection.GetAsync<Poll>(pollId).ConfigureAwait(false);
            poll.MessageId = messageId.ToSigned();
            await connection.UpdateAsync(poll).ConfigureAwait(false);
        }

        #endregion Polling

        #region SuicideKings

        public async Task<int> AddSuicideKingsDropAsync(int listId, int memberId, DateTimeOffset timeStamp, string? reason)
        {
            var drop = new SuicideKingsDrop
            {
                ListId = listId,
                MemberId = memberId,
                Reason = reason,
                TimeStamp = timeStamp
            };
            await connection.InsertAsync(drop).ConfigureAwait(false);
            return drop.DropId;
        }

        public Task AddSuicideKingsDropWitnessAsync(int dropId, int memberId) =>
            connection.InsertAsync(new SuicideKingsDropWitness
            {
                DropId = dropId,
                MemberId = memberId
            });

        public async Task<int> AddSuicideKingsListAsync(ulong channelId, string name)
        {
            var list = new SuicideKingsList
            {
                ChannelId = channelId.ToSigned(),
                Name = name
            };
            await connection.InsertAsync(list).ConfigureAwait(false);
            return list.ListId;
        }

        public async Task<int> AddSuicideKingsMemberAsync(ulong channelId, string name)
        {
            var member = new SuicideKingsMember
            {
                ChannelId = channelId.ToSigned(),
                Name = name
            };
            await connection.InsertOrReplaceAsync(member).ConfigureAwait(false);
            return member.MemberId;
        }

        public async Task AddSuicideKingsRoleAsync(ulong guildId, ulong roleId) =>
            await connection.InsertAsync(new SuicideKingsRole
            {
                GuildId = guildId.ToSigned(),
                RoleId = roleId.ToSigned()
            }).ConfigureAwait(false);

        public async Task<int?> GetSuicideKingsListIdByNameAsync(ulong channelId, string name)
        {
            var cId = channelId.ToSigned();
            var upperName = name.ToUpperInvariant();
            return (await connection.Table<SuicideKingsList>().Where(l => l.ChannelId == cId && l.Name != null && l.Name.ToUpper() == upperName).FirstOrDefaultAsync().ConfigureAwait(false))?.ListId;
        }

        public async Task<IReadOnlyList<(int listId, string name)>> GetSuicideKingsListsAsync(ulong channelId)
        {
            var id = channelId.ToSigned();
            return (await connection.Table<SuicideKingsList>().Where(l => l.ChannelId == id).OrderBy(l => l.Name).ToListAsync().ConfigureAwait(false)).Select(l => (l.ListId, l.Name)).ToImmutableArray();
        }

        public async Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsListMembersInPositionOrderAsync(int listId) =>
            (await connection.QueryAsync<SuicideKingsMember>("select m.ChannelId, m.MemberId, m.Name from SuicideKingsListEntry le join SuicideKingsMember m on m.MemberId = le.MemberId where le.ListId = ? and le.Position >= 0 order by le.Position", listId).ConfigureAwait(false)).Select(m => (m.MemberId, m.Name)).ToImmutableArray();

        public async Task<int?> GetSuicideKingsMemberIdByNameAsync(ulong channelId, string name)
        {
            var cId = channelId.ToSigned();
            var upperName = name.ToUpperInvariant();
            return (await connection.Table<SuicideKingsMember>().Where(m => m.ChannelId == cId && m.Name != null && m.Name.ToUpper() == upperName && m.Retired == null).FirstOrDefaultAsync().ConfigureAwait(false))?.MemberId;
        }

        public async Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsMembersAsync(ulong channelId)
        {
            var id = channelId.ToSigned();
            return (await connection.Table<SuicideKingsMember>().Where(m => m.ChannelId == id && m.Retired == null).OrderBy(m => m.Name).ToListAsync().ConfigureAwait(false)).Select(m => (m.MemberId, m.Name)).ToImmutableArray();
        }

        public async Task<IReadOnlyList<ulong>> GetSuicideKingsRolesAsync(ulong guildId)
        {
            var roles = new List<ulong>();
            var gId = guildId.ToSigned();
            foreach (var roleId in (await connection.Table<SuicideKingsRole>().Where(r => r.GuildId == gId).ToListAsync().ConfigureAwait(false)).Select(r => r.RoleId))
                roles.Add(roleId.ToUnsigned());
            return roles;
        }

        public async Task RemoveSuicideKingsListAsync(int listId)
        {
            await connection.Table<SuicideKingsDrop>().DeleteAsync(s => s.ListId == listId).ConfigureAwait(false);
            await connection.Table<SuicideKingsListEntry>().DeleteAsync(le => le.ListId == listId).ConfigureAwait(false);
            await connection.Table<SuicideKingsList>().DeleteAsync(l => l.ListId == listId).ConfigureAwait(false);
        }

        public async Task RemoveSuicideKingsRoleAsync(ulong guildId, ulong roleId)
        {
            var gId = guildId.ToSigned();
            var rId = roleId.ToSigned();
            await connection.Table<SuicideKingsRole>().DeleteAsync(r => r.GuildId == gId && r.RoleId == rId).ConfigureAwait(false);
        }

        public async Task RetireSuicideKingsMemberAsync(int memberId)
        {
            var member = await connection.GetAsync<SuicideKingsMember>(memberId).ConfigureAwait(false);
            member.Retired = DateTimeOffset.UtcNow;
            var entries = await connection.Table<SuicideKingsListEntry>().Where(e => e.MemberId == memberId).ToListAsync().ConfigureAwait(false);
            foreach (var entry in entries)
                entry.Position = -1;
            await connection.UpdateAsync(member).ConfigureAwait(false);
            await connection.UpdateAllAsync(entries).ConfigureAwait(false);
        }

        public async Task SetSuicideKingsListMemberEntryAsync(int listId, int memberId, int position)
        {
            if (await connection.Table<SuicideKingsListEntry>().Where(le => le.ListId == listId && le.MemberId == memberId).FirstOrDefaultAsync().ConfigureAwait(false) is null)
                await connection.InsertAsync(new SuicideKingsListEntry
                {
                    ListId = listId,
                    MemberId = memberId,
                    Position = position
                });
            else
                await connection.ExecuteAsync("update SuicideKingsListEntry set Position = ? where ListId = ? and MemberId = ?", position, listId, memberId).ConfigureAwait(false);
        }

        #endregion SuicideKings
    }
}
