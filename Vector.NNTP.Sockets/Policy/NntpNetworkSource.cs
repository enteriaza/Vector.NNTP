// <copyright file="NntpNetworkSource.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: parsed IP or CIDR network source for transit peer matching.

using System.Globalization;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Parsed IP address or CIDR range used to match client addresses for transit peers.
    /// </summary>
    internal readonly struct NntpNetworkSource
    {
        private readonly IPAddress _network;
        private readonly int _prefixLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpNetworkSource"/> struct.
        /// </summary>
        /// <param name="network">Network address.</param>
        /// <param name="prefixLength">Prefix length in bits.</param>
        private NntpNetworkSource(IPAddress network, int prefixLength)
        {
            _network = network;
            _prefixLength = prefixLength;
        }

        /// <summary>
        /// Gets the network address for overlap checks.
        /// </summary>
        internal IPAddress Network => _network;

        /// <summary>
        /// Parses an IP or CIDR entry from configuration text.
        /// </summary>
        /// <param name="text">Literal IP or CIDR (for example <c>10.0.0.0/8</c>).</param>
        /// <param name="source">Parsed source when successful.</param>
        /// <returns><see langword="true"/> when <paramref name="text"/> parsed successfully.</returns>
        internal static bool TryParse(string text, out NntpNetworkSource source)
        {
            source = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int slash = text.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                if (!IPAddress.TryParse(text, out IPAddress? ip))
                {
                    return false;
                }

                int bits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
                source = new NntpNetworkSource(ip, bits);
                return true;
            }

            string ipText = text.Substring(0, slash);
            string prefixText = text.Substring(slash + 1);
            if (!IPAddress.TryParse(ipText, out IPAddress? network))
            {
                return false;
            }

            if (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out int prefix))
            {
                return false;
            }

            int max = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (prefix < 0 || prefix > max)
            {
                return false;
            }

            source = new NntpNetworkSource(network, prefix);
            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="address"/> is contained in this source.
        /// </summary>
        /// <param name="address">Client address to test.</param>
        /// <returns><see langword="true"/> when the address matches.</returns>
        internal bool Contains(IPAddress address)
        {
            if (address.AddressFamily != _network.AddressFamily)
            {
                return false;
            }

            ReadOnlySpan<byte> addrBytes = address.GetAddressBytes();
            ReadOnlySpan<byte> netBytes = _network.GetAddressBytes();
            int fullBytes = _prefixLength / 8;
            int remainingBits = _prefixLength % 8;

            if (fullBytes > 0 && !addrBytes.Slice(0, fullBytes).SequenceEqual(netBytes.Slice(0, fullBytes)))
            {
                return false;
            }

            if (remainingBits == 0)
            {
                return true;
            }

            byte mask = (byte)(0xFF << (8 - remainingBits));
            return (addrBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
        }

        /// <summary>
        /// Determines whether this source overlaps another (any address could match both).
        /// </summary>
        /// <param name="other">Other source.</param>
        /// <returns><see langword="true"/> when ranges overlap.</returns>
        internal bool Overlaps(NntpNetworkSource other)
        {
            if (_network.AddressFamily != other._network.AddressFamily)
            {
                return false;
            }

            return Contains(other._network) || other.Contains(_network);
        }
    }
}
