// <copyright file="NntpDotStuffingReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: legacy wrapper delegating to <see cref="NntpArticleBodyReader"/> for benchmarks and migration.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Reads a dot-stuffed article body terminated by CRLF.CRLF. on its own line.
    /// </summary>
    internal static class NntpDotStuffingReader
    {
        /// <summary>
        /// Reads a dot-stuffed body into a byte array via the optimized article body reader.
        /// </summary>
        /// <param name="lineReader">Line reader for the session (provides pipe and Rx accounting).</param>
        /// <param name="maxBodyBytes">Maximum decoded body size (0 disables the limit).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decoded body bytes.</returns>
        internal static ValueTask<NntpArticleBodyReadResult> ReadBodyAsync(
            NntpLineReader lineReader,
            long maxBodyBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(lineReader);
            return lineReader.ReadDotStuffedBodyAsync(maxBodyBytes, cancellationToken);
        }

        /// <summary>
        /// Discards a pipelined dot-stuffed body without allocating or enforcing article size limits.
        /// </summary>
        /// <param name="lineReader">Line reader for the session (provides pipe and Rx accounting).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the terminator line is consumed.</returns>
        internal static ValueTask DrainBodyAsync(NntpLineReader lineReader, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(lineReader);
            return lineReader.DrainDotStuffedBodyAsync(cancellationToken);
        }
    }
}
