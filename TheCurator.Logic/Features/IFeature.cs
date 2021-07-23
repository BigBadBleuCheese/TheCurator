using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheCurator.Logic.Features
{
    public interface IFeature : IDisposable
    {
        Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs);
    }
}
