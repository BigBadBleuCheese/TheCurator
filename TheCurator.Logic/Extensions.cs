using System;

namespace TheCurator.Logic
{
    public static class Extensions
    {
        public static long ToSigned(this ulong ul) => BitConverter.ToInt64(BitConverter.GetBytes(ul), 0);

        public static ulong ToUnsigned(this long l) => BitConverter.ToUInt64(BitConverter.GetBytes(l), 0);
    }
}
