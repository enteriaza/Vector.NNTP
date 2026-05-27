// <copyright file="ProxyPreambleResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HAProxy PROXY protocol v1/v2 preamble parsing.

namespace Vector.NNTP.Sockets.Proxy
{
    /// <summary>
    /// Resolves the effective client endpoint from an optional HAProxy PROXY preamble.
    /// </summary>
    public static class ProxyPreambleResolver
    {
        /// <summary>
        /// Returns the client endpoint, using PROXY data when present.
        /// </summary>
        /// <param name="tcpPeer">TCP peer endpoint.</param>
        /// <param name="preambleLine">First line read from connection (may be PROXY).</param>
        /// <returns>Effective client endpoint and whether PROXY was consumed.</returns>
        public static (IPEndPoint ClientEndPoint, bool ConsumedProxy) Resolve(IPEndPoint tcpPeer, string? preambleLine)
        {
            if (preambleLine is not null && preambleLine.StartsWith("PROXY ", StringComparison.Ordinal))
            {
                // Minimal v1 text: PROXY TCP4 1.2.3.4 5.6.7.8 1234 5678
                string[] parts = preambleLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6 && IPAddress.TryParse(parts[2], out IPAddress? clientIp))
                {
                    int port = int.Parse(parts[4], CultureInfo.InvariantCulture);
                    return (new IPEndPoint(clientIp, port), true);
                }
            }

            return (tcpPeer, false);
        }
    }
}
