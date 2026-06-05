// <copyright file="ProxyTrustedSource.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: trusted source parsing for HAProxy PROXY protocol.

namespace Vector.NNTP.Sockets.Proxy
{
    /// <summary>
    /// Represents a trusted first-hop source for accepting PROXY protocol preambles.
    /// </summary>
    internal readonly struct ProxyTrustedSource
    {
        private readonly IPAddress _network;
        private readonly int _prefixLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProxyTrustedSource"/> struct.
        /// </summary>
        /// <param name="network">Network address.</param>
        /// <param name="prefixLength">Prefix length in bits.</param>
        private ProxyTrustedSource(IPAddress network, int prefixLength)
        {
            _network = network;
            _prefixLength = prefixLength;
        }

        /// <summary>
        /// Parses a trusted source entry from configuration.
        /// </summary>
        /// <param name="text">IP or CIDR entry.</param>
        /// <param name="source">Parsed trusted source.</param>
        /// <returns>True when parsed successfully.</returns>
        internal static bool TryParse(string text, out ProxyTrustedSource source)
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
                source = new ProxyTrustedSource(ip, bits);
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

            source = new ProxyTrustedSource(network, prefix);
            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="address"/> is contained within this trusted source.
        /// </summary>
        /// <param name="address">Address to evaluate.</param>
        /// <returns>True when the address is trusted.</returns>
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
    }
}

