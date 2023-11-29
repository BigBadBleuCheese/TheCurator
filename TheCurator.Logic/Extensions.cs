using System;

namespace TheCurator.Logic
{
    public static class Extensions
    {
        public static Task DenyPermissionAsync(this SocketSlashCommand command) =>
            command.RespondAsync("Do not touch the displays.", ephemeral: true);

        public static async Task<bool> RequireAdministrativeUserAsync(this SocketSlashCommand command, IBot bot)
        {
            if (bot.IsAdministrativeUser(command.User))
                return true;
            await DenyPermissionAsync(command).ConfigureAwait(false);
            return false;
        }

        public static long ToSigned(this ulong ul) => BitConverter.ToInt64(BitConverter.GetBytes(ul), 0);

        public static ulong ToUnsigned(this long l) => BitConverter.ToUInt64(BitConverter.GetBytes(l), 0);
    }
}
