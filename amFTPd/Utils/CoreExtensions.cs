using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace amFTPd.Utils
{
    internal static class CoreExtensions
    {
        public static bool HasFlag(this ImmutableHashSet<char> flags, char flag)
            => flags.Contains(char.ToUpperInvariant(flag));
    }
}
