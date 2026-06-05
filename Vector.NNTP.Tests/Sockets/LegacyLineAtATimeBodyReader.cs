// <copyright file="LegacyLineAtATimeBodyReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: pre-optimization line-at-a-time body reader for benchmark and allocation comparisons.

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Legacy per-line dot-stuffed body reader retained for performance regression comparisons.
    /// </summary>
    internal static class LegacyLineAtATimeBodyReader
    {
        /// <summary>
        /// Reads a dot-stuffed body one <see cref="Vector.NNTP.Sockets.Transport.NntpLineReader.ReadLineBytesAsync"/> call per line.
        /// </summary>
        /// <param name="lineReader">Line reader.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decoded body bytes.</returns>
        internal static async ValueTask<byte[]> ReadBodyAsync(
            Vector.NNTP.Sockets.Transport.NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(lineReader);
            using MemoryStream ms = new();
            while (true)
            {
                Vector.NNTP.Sockets.Transport.NntpByteLineReadResult line =
                    await lineReader.ReadLineBytesAsync(cancellationToken).ConfigureAwait(false);
                if (line.IsCompleted)
                {
                    break;
                }

                if (line.IsDotTerminator)
                {
                    break;
                }

                ReadOnlyMemory<byte> payload = line.Line;
                if (line.IsDotStuffed)
                {
                    payload = payload.Slice(1);
                }

                ms.Write(payload.Span);
                ms.WriteByte((byte)'\r');
                ms.WriteByte((byte)'\n');
            }

            return ms.ToArray();
        }
    }
}
