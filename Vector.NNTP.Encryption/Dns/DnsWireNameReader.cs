//-----------------------------------------------------------------------
// <copyright file="DnsWireNameReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Text;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Reads a DNS domain name from a wire-format packet (RFC 1035), following compression pointers.
    /// </summary>
    internal static class DnsWireNameReader
    {
        /// <summary>
        /// The maximum number of compression pointer hops.
        /// </summary>
        private const int MaxPointerHops = 128;

        /// <summary>
        /// The maximum length of the expanded name in bytes.
        /// </summary>
        private const int MaxExpandedNameLengthBytes = 255;

        /// <summary>
        /// Reads a domain name starting at <paramref name="offset"/>; updates <paramref name="offset"/> to the
        /// first byte after the name encoding (after the root label or compression jump return).
        /// </summary>
        /// <param name="packet">The full DNS response buffer.</param>
        /// <param name="offset">The current read position; advanced past the name on return.</param>
        /// <param name="name">The name read from the packet.</param>
        /// <returns><see langword="true"/> if the name was successfully read; <see langword="false"/> if the buffer is
        /// too short or the name is malformed.</returns>
        public static bool TryReadDomainName(ReadOnlySpan<byte> packet, ref int offset, out string name)
        {
            name = string.Empty;
            List<string>? labels = null;
            bool jumped = false;
            int jumpBack = 0;
            int pos = offset;
            int expandedBytes = 0;

            for (int hop = 0; hop < MaxPointerHops; hop++)
            {
                if (pos >= packet.Length)
                {
                    return false;
                }

                byte len = packet[pos];
                if ((len & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= packet.Length)
                    {
                        return false;
                    }

                    if (!jumped)
                    {
                        jumped = true;
                        jumpBack = pos + 2;
                    }

                    pos = ((len & 0x3F) << 8) | packet[pos + 1];
                    if (pos >= packet.Length)
                    {
                        return false;
                    }

                    continue;
                }

                if (len == 0)
                {
                    pos++;
                    offset = jumped ? jumpBack : pos;
                    name = labels is null || labels.Count == 0 ? string.Empty : string.Join(".", labels);
                    return true;
                }

                if (len > 63)
                {
                    return false;
                }

                if (pos + 1 + len > packet.Length)
                {
                    return false;
                }

                pos++;
                labels ??= [];

                // Expanded name length limit (RFC 1035: 255 bytes including length octets); we conservatively
                // cap the dotted string to 255 to avoid pathological packet-induced growth.
                expandedBytes += (labels.Count == 0 ? 0 : 1) + len;
                if (expandedBytes > MaxExpandedNameLengthBytes)
                {
                    return false;
                }

                labels.Add(Encoding.ASCII.GetString(packet.Slice(pos, len)));
                pos += len;
            }

            return false;
        }
    }
}
