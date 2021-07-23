using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace TheCurator.Logic
{
    public interface IBot : IDisposable
    {
        DiscordSocketClient Client { get; }

        Task InitializeAsync(string token);

        bool IsAdministrativeUser(IUser user);
    }
}
