// <copyright file="NntpDotStuffingReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: reads dot-stuffed multi-line bodies for POST and IHAVE.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Reads a dot-stuffed article body terminated by CRLF.CRLF. on its own line.
    /// </summary>
    internal static class NntpDotStuffingReader
    {
        /// <summary>
        /// Reads a dot-stuffed body into a pooled buffer.
        /// </summary>
        /// <param name="lineReader">Line reader for the session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decoded body bytes.</returns>
        internal static async ValueTask<byte[]> ReadBodyAsync(NntpLineReader lineReader, CancellationToken cancellationToken)
        {
            using MemoryStream ms = new();
            while (true)
            {
                string? line = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line == ".")
                {
                    break;
                }

                if (line.StartsWith("..", StringComparison.Ordinal))
                {
                    line = line[1..];
                }

                byte[] bytes = Encoding.ASCII.GetBytes(line);
                ms.Write(bytes, 0, bytes.Length);
                ms.WriteByte((byte)'\r');
                ms.WriteByte((byte)'\n');
            }

            return ms.ToArray();
        }
    }
}
