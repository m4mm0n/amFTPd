/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CidrBlock.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:01:23
 *  Last Modified:  2025-12-11 04:26:20
 *  CRC32:          0x11787BBA
 *  
 *  Description:
 *      Simple CIDR block representation (IPv4 only for now).
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using System.Net;
using System.Net.Sockets;

namespace amFTPd.Security.BanList
{
    /// <summary>
    /// Simple CIDR block representation (IPv4 only for now).
    /// </summary>
    internal readonly struct CidrBlock
    {
        public IPAddress Network { get; }
        public int PrefixLength { get; }

        public CidrBlock(IPAddress network, int prefixLength)
        {
            if (network.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("Only IPv4 CIDR blocks are supported.");

            if (prefixLength is < 0 or > 32)
                throw new ArgumentOutOfRangeException(nameof(prefixLength));

            Network = network;
            PrefixLength = prefixLength;
        }

        public bool Contains(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var networkBytes = Network.GetAddressBytes();
            var addrBytes = address.GetAddressBytes();

            var networkValue = BitConverter.ToUInt32(networkBytes, 0);
            var addrValue = BitConverter.ToUInt32(addrBytes, 0);

            // Convert to host order if needed
            if (BitConverter.IsLittleEndian)
            {
                networkValue = ReverseBytes(networkValue);
                addrValue = ReverseBytes(addrValue);
            }

            var mask = PrefixLength == 0
                ? 0u
                : uint.MaxValue << (32 - PrefixLength);

            return (networkValue & mask) == (addrValue & mask);
        }

        private static uint ReverseBytes(uint value) =>
            ((value & 0x000000FFu) << 24) |
            ((value & 0x0000FF00u) << 8) |
            ((value & 0x00FF0000u) >> 8) |
            ((value & 0xFF000000u) >> 24);

        public override string ToString()
            => $"{Network}/{PrefixLength}";
    }
}
