using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheCurator.Logic.Data
{
    public interface IDataStore : IAsyncDisposable
    {
        Task ConnectAsync();

        Task DisconnectAsync();

        #region Counting

        Task<(int? count, ulong? lastAuthorId)> GetCountingChannelCountAsync(ulong channelId);

        Task SetCountingChannelCountAsync(ulong channelId, int? count, ulong? lastAuthorId);

        #endregion Counting

        #region SuicideKings

        Task<int> AddSuicideKingsDropAsync(int listId, int memberId, DateTimeOffset timeStamp, string? reason);

        Task AddSuicideKingsDropWitnessAsync(int dropId, int memberId);

        Task<int> AddSuicideKingsListAsync(ulong channelId, string name);

        Task<int> AddSuicideKingsMemberAsync(ulong channelId, string name);

        Task AddSuicideKingsRoleAsync(ulong guildId, ulong roleId);

        Task<int?> GetSuicideKingsListIdByNameAsync(ulong channelId, string name);

        Task<IReadOnlyList<(int listId, string name)>> GetSuicideKingsListsAsync(ulong channelId);

        Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsListMembersInPositionOrderAsync(int listId);

        Task<int?> GetSuicideKingsMemberIdByNameAsync(ulong channelId, string name);

        Task<IReadOnlyList<(int memberId, string name)>> GetSuicideKingsMembersAsync(ulong channelId);

        Task<IReadOnlyList<ulong>> GetSuicideKingsRolesAsync(ulong guildId);

        Task RemoveSuicideKingsListAsync(int listId);

        Task RemoveSuicideKingsRoleAsync(ulong guildId, ulong roleId);

        Task RetireSuicideKingsMemberAsync(int memberId);

        Task SetSuicideKingsListMemberEntryAsync(int listId, int memberId, int position);

        #endregion SuicideKings
    }
}
