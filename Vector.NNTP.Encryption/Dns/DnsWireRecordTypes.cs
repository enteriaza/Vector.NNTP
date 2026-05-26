//-----------------------------------------------------------------------
// <copyright file="DnsWireRecordTypes.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// DNS resource record QTYPE / TYPE constants (RFC 1035).
    /// </summary>
    internal static class DnsWireRecordTypes
    {
        /// <summary>NS record.</summary>
        public const ushort Ns = 2;

        /// <summary>A record (IPv4).</summary>
        public const ushort A = 1;

        /// <summary>AAAA record (IPv6).</summary>
        public const ushort Aaaa = 28;

        /// <summary>TXT record.</summary>
        public const ushort Txt = 16;
    }
}
