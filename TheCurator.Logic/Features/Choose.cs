using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace TheCurator.Logic.Features
{
    public class Choose : IFeature
    {
        public void Dispose()
        {
        }

        public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
        {
            if (commandArgs.Count >= 2 && commandArgs[0].ToUpperInvariant() == "CHOOSE")
            {
                var choices = commandArgs.Skip(1).ToImmutableArray();
                await message.Channel.SendMessageAsync(choices[new Random().Next(choices.Length)], messageReference: new MessageReference(message.Id));
                return true;
            }
            return false;
        }
    }
}
