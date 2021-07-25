using Cogs.Disposal;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data.SQLite
{
    public class DataStore : AsyncDisposable, IDataStore
    {
        public DataStore()
        {
            connection = new SQLiteAsyncConnection(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "data.sqlite"));
            discordIdById = new ConcurrentDictionary<int, ulong>();
            idByDiscordId = new ConcurrentDictionary<ulong, int>();
        }

        readonly SQLiteAsyncConnection connection;
        readonly ConcurrentDictionary<int, ulong> discordIdById;
        readonly ConcurrentDictionary<ulong, int> idByDiscordId;

        void CacheIdAndDiscordId(int id, ulong discordId)
        {
            discordIdById.AddOrUpdate(id, discordId, (k, v) => discordId);
            idByDiscordId.AddOrUpdate(discordId, id, (k, v) => id);
        }

        public async Task ConnectAsync()
        {
            var schemaVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;").ConfigureAwait(false);
            var readSchemaVersion = schemaVersion;
            if (schemaVersion == 0)
            {
                await connection.CreateTableAsync<CountingChannel>().ConfigureAwait(false);
                await connection.CreateTableAsync<Snowflake>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsList>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsListEntry>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsMember>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsRole>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsDrop>().ConfigureAwait(false);
                await connection.CreateTableAsync<SuicideKingsDropWitness>().ConfigureAwait(false);
                schemaVersion = 1;
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

        async Task<int> GetIdFromDiscordIdAsync(ulong discordId)
        {
            if (!idByDiscordId.TryGetValue(discordId, out var id))
            {
                var discordIdStr = discordId.ToString();
                var snowflake = await connection.Table<Snowflake>().Where(sf => sf.DiscordId == discordIdStr).FirstOrDefaultAsync().ConfigureAwait(false);
                if (snowflake == null)
                {
                    snowflake = new Snowflake { DiscordId = discordIdStr };
                    await connection.InsertAsync(snowflake).ConfigureAwait(false);
                }
                id = snowflake.Id;
                CacheIdAndDiscordId(id, discordId);
            }
            return id;
        }

        async Task<ulong> GetDiscordIdFromIdAsync(int id)
        {
            if (!discordIdById.TryGetValue(id, out var discordId))
            {
                discordId = ulong.Parse((await connection.GetAsync<Snowflake>(id).ConfigureAwait(false)).DiscordId);
                CacheIdAndDiscordId(id, discordId);
            }
            return discordId;
        }

        #region Counting

        public async Task<(int? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId)
        {
            var id = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            var countingChannel = await connection.Table<CountingChannel>().FirstOrDefaultAsync(cc => cc.ChannelId == id).ConfigureAwait(false);
            return countingChannel is null ? (null, null) : (countingChannel.Count, await GetDiscordIdFromIdAsync(countingChannel.LastAuthorId).ConfigureAwait(false));
        }

        public async Task SetCountingChannelCountAsync(ulong channelId, int? count, ulong? lastAuthorId)
        {
            if (count is { } nonNullCount && lastAuthorId is { } nonNullLastAuthorId)
                await connection.InsertOrReplaceAsync(new CountingChannel
                {
                    ChannelId = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false),
                    Count = nonNullCount,
                    LastAuthorId = await GetIdFromDiscordIdAsync(nonNullLastAuthorId).ConfigureAwait(false),
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
                ChannelId = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false),
                Name = name
            };
            await connection.InsertAsync(list).ConfigureAwait(false);
            return list.ListId;
        }

        public async Task<int> AddSuicideKingsMemberAsync(ulong channelId, string name)
        {
            var member = new SuicideKingsMember
            {
                ChannelId = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false),
                Name = name
            };
            await connection.InsertAsync(member).ConfigureAwait(false);
            return member.MemberId;
        }

        public async Task AddSuicideKingsRoleAsync(ulong guildId, ulong roleId) =>
            await connection.InsertAsync(new SuicideKingsRole
            {
                GuildId = await GetIdFromDiscordIdAsync(guildId).ConfigureAwait(false),
                RoleId = await GetIdFromDiscordIdAsync(roleId).ConfigureAwait(false)
            }).ConfigureAwait(false);

        public async Task<int?> GetSuicideKingsListIdByNameAsync(ulong channelId, string name)
        {
            var cId = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            var upperName = name.ToUpperInvariant();
            return (await connection.Table<SuicideKingsList>().Where(l => l.Name != null && l.Name.ToUpper() == upperName).FirstOrDefaultAsync().ConfigureAwait(false))?.ListId;
        }

        public async Task<IReadOnlyList<(int listId, string name)>> GetSuicideKingsListsAsync(ulong channelId)
        {
            var id = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            return (await connection.Table<SuicideKingsList>().Where(l => l.ChannelId == id).OrderBy(l => l.Name).ToListAsync().ConfigureAwait(false)).Select(l => (l.ListId, l.Name)).ToImmutableArray();
        }

        public async Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsListMembersInPositionOrderAsync(int listId) =>
            (await connection.QueryAsync<SuicideKingsMember>("select m.ChannelId, m.MemberId, m.Name from SuicideKingsListEntry le join SuicideKingsMember m on m.MemberId = le.MemberId where le.ListId = ? and m.Retired is null order by le.Position", listId).ConfigureAwait(false)).Select(m => (m.MemberId, m.Name)).ToImmutableArray();

        public async Task<int?> GetSuicideKingsMemberIdByNameAsync(ulong channelId, string name)
        {
            var cId = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            var upperName = name.ToUpperInvariant();
            return (await connection.Table<SuicideKingsMember>().Where(m => m.Name != null && m.Name.ToUpper() == upperName).FirstOrDefaultAsync().ConfigureAwait(false))?.MemberId;
        }

        public async Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsMembersAsync(ulong channelId)
        {
            var id = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            return (await connection.Table<SuicideKingsMember>().Where(m => m.ChannelId == id && m.Retired == null).OrderBy(m => m.Name).ToListAsync().ConfigureAwait(false)).Select(m => (m.MemberId, m.Name)).ToImmutableArray();
        }

        public async Task<IReadOnlyList<ulong>> GetSuicideKingsRolesAsync(ulong guildId)
        {
            var roles = new List<ulong>();
            var gId = await GetIdFromDiscordIdAsync(guildId).ConfigureAwait(false);
            foreach (var roleId in (await connection.Table<SuicideKingsRole>().Where(r => r.GuildId == gId).ToListAsync().ConfigureAwait(false)).Select(r => r.RoleId))
                roles.Add(await GetDiscordIdFromIdAsync(roleId).ConfigureAwait(false));
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
            var gId = await GetIdFromDiscordIdAsync(guildId).ConfigureAwait(false);
            var rId = await GetIdFromDiscordIdAsync(roleId).ConfigureAwait(false);
            await connection.Table<SuicideKingsRole>().DeleteAsync(r => r.GuildId == gId && r.RoleId == rId).ConfigureAwait(false);
        }

        public async Task RetireSuicideKingsMemberAsync(int memberId)
        {
            var member = await connection.GetAsync<SuicideKingsMember>(memberId).ConfigureAwait(false);
            member.Retired = System.DateTimeOffset.UtcNow;
            await connection.UpdateAsync(member).ConfigureAwait(false);
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
