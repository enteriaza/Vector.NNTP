// <copyright file="ProxyProtocolPreambleTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: PROXY protocol v1/v2 parsing tests.

using System.Buffers.Binary;
using System.Text;
using Vector.NNTP.Sockets.Proxy;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Validates PROXY protocol preamble parsing and bounds.
    /// </summary>
    [TestFixture]
    public sealed class ProxyProtocolPreambleTests
    {
        /// <summary>
        /// Verifies a minimal PROXY v1 line updates the effective client endpoint.
        /// </summary>
        [Test]
        public void V1_MinimalLine_ParsesClientEndpoint()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("PROXY TCP4 1.2.3.4 5.6.7.8 1234 5678\r\nNEXT\r\n");
            IPEndPoint peer = new(IPAddress.Loopback, 119);

            bool consumed = ProxyProtocolPreamble.TryParse(bytes, bytes.Length, peer, out int used, out IPEndPoint client);
            Assert.That(consumed, Is.True);
            Assert.That(used, Is.GreaterThan(0));
            Assert.That(client.Address.ToString(), Is.EqualTo("1.2.3.4"));
            Assert.That(client.Port, Is.EqualTo(1234));
        }

        /// <summary>
        /// Verifies a minimal PROXY v2 IPv4 frame updates the effective client endpoint.
        /// </summary>
        [Test]
        public void V2_Ipv4Proxy_ParsesClientEndpoint()
        {
            // Signature (12) + ver/cmd (1) + fam/proto (1) + len (2) + payload.
            byte[] sig =
            [
                0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A,
            ];

            Span<byte> header = stackalloc byte[16];
            sig.CopyTo(header);
            header[12] = 0x21; // v2 + PROXY
            header[13] = 0x11; // AF_INET + STREAM

            Span<byte> payload = stackalloc byte[12];
            payload[0] = 1;
            payload[1] = 2;
            payload[2] = 3;
            payload[3] = 4; // src
            payload[4] = 5;
            payload[5] = 6;
            payload[6] = 7;
            payload[7] = 8; // dst
            BinaryPrimitives.WriteUInt16BigEndian(payload.Slice(8, 2), 1234); // src port
            BinaryPrimitives.WriteUInt16BigEndian(payload.Slice(10, 2), 5678); // dst port
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(14, 2), 12);

            byte[] buffer = new byte[16 + 12 + 4];
            header.CopyTo(buffer);
            payload.CopyTo(buffer.AsSpan(16));

            IPEndPoint peer = new(IPAddress.Loopback, 119);
            bool consumed = ProxyProtocolPreamble.TryParse(buffer, 28, peer, out int used, out IPEndPoint client);
            Assert.That(consumed, Is.True);
            Assert.That(used, Is.EqualTo(28));
            Assert.That(client.Address.ToString(), Is.EqualTo("1.2.3.4"));
            Assert.That(client.Port, Is.EqualTo(1234));
        }

        /// <summary>
        /// Verifies an oversized PROXY v2 frame is treated as consumed (fail-safe) and does not allocate.
        /// </summary>
        [Test]
        public void V2_OversizeLength_IsHardBounded()
        {
            byte[] sig =
            [
                0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A,
            ];
            byte[] header = new byte[16];
            Array.Copy(sig, header, sig.Length);
            header[12] = 0x21;
            header[13] = 0x11;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(14, 2), (ushort)(ProxyProtocolPreamble.MaxV2FrameLength + 1));

            IPEndPoint peer = new(IPAddress.Loopback, 119);
            bool consumed = ProxyProtocolPreamble.TryParse(header, header.Length, peer, out int used, out IPEndPoint client);
            Assert.That(consumed, Is.True);
            Assert.That(used, Is.EqualTo(16));
            Assert.That(client, Is.EqualTo(peer));
        }
    }
}
