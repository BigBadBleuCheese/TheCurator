using System;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data
{
    public interface IDataStore : IAsyncDisposable
    {
        Task ConnectAsync();

        Task DisconnectAsync();

        Task<(int? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId);

        Task SetCountingChannelCountAsync(ulong channelId, int? count, ulong? lastAuthorId);
    }
}
