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
