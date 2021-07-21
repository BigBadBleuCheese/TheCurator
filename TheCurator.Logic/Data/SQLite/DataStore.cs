using Cogs.Disposal;
using SQLite;
using System.IO;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data.SQLite
{
    public class DataStore : AsyncDisposable, IDataStore
    {
        public DataStore() => connection = new SQLiteAsyncConnection(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "data.sqlite"));

        readonly SQLiteAsyncConnection connection;

        protected override async ValueTask<bool> DisposeAsync(bool disposing)
        {
            if (disposing)
                await DisconnectAsync().ConfigureAwait(false);
            return true;
        }

        public async Task ConnectAsync()
        {
            await connection.CreateTableAsync<Schema>().ConfigureAwait(false);
            var schema = await connection.Table<Schema>().FirstOrDefaultAsync().ConfigureAwait(false);
            if (schema == default)
                schema = new Schema { Version = 0 };
            await connection.InsertOrReplaceAsync(schema).ConfigureAwait(false);
            await connection.CreateTableAsync<CountingChannel>().ConfigureAwait(false);
        }

        public Task DisconnectAsync() => connection.CloseAsync();

        public async Task<(uint? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId)
        {
            var channelIdStr = channelId.ToString();
            var countingChannel = await connection.Table<CountingChannel>().Where(cc => cc.ChannelId == channelIdStr).FirstOrDefaultAsync().ConfigureAwait(false);
            return countingChannel?.Count is { } count && ulong.TryParse(countingChannel?.LastAuthorId, out var lastAuthorId) ? (count, lastAuthorId) : (null, null);
        }

        public async Task SetCountingChannelCountAsync(ulong channelId, uint? count, ulong? lastAuthorId)
        {
            var channelIdStr = channelId.ToString();
            if (count is { } nonNullCount && lastAuthorId is not null)
                await connection.InsertOrReplaceAsync(new CountingChannel
                {
                    ChannelId = channelIdStr,
                    Count = nonNullCount,
                    LastAuthorId = lastAuthorId.ToString()
                }).ConfigureAwait(false);
            else
                await connection.Table<CountingChannel>().Where(cc => cc.ChannelId == channelIdStr).DeleteAsync().ConfigureAwait(false);
        }
    }
}
