using Cogs.Disposal;
using SQLite;
using System.Collections.Concurrent;
using System.IO;
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
                await connection.CreateTableAsync<SuicideKingsSuicide>().ConfigureAwait(false);
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

        public async Task<(int? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId)
        {
            var id = await GetIdFromDiscordIdAsync(channelId).ConfigureAwait(false);
            var countingChannel = await connection.Table<CountingChannel>().FirstOrDefaultAsync(cc => cc.ChannelId == id).ConfigureAwait(false);
            return countingChannel is null ? (null, null) : (countingChannel.Count, await GetDiscordIdFromIdAsync(countingChannel.LastAuthorId).ConfigureAwait(false));
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
    }
}
