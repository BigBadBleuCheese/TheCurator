using System;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data
{
    public interface IDataStore : IAsyncDisposable
    {
        Task ConnectAsync();

        Task DisconnectAsync();

        Task<(uint? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId);

        Task SetCountingChannelCountAsync(ulong channelId, uint? count, ulong? lastAuthorId);
    }
}
