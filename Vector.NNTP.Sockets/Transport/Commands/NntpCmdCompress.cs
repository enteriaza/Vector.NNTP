// <copyright file="NntpCmdCompress.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: COMPRESS DEFLATE command handler (RFC 8054).

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Session;

    /// <summary>
    /// Handles the NNTP COMPRESS command.
    /// </summary>
    internal static class NntpCmdCompress
    {
        /// <summary>
        /// Activates DEFLATE compression on the session transport when advertised.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="activateCompression">Callback to wrap the duplex pipe with deflate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            string line,
            Func<CancellationToken, ValueTask> activateCompression,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(activateCompression);
            if (!session.Options.EnableCompressDeflate)
            {
                await session.Writer.WriteLineAsync("502 COMPRESS not available", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (session.State.IsCompressionActive)
            {
                await session.Writer.WriteLineAsync("502 Compression already active", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!line.Contains("DEFLATE", StringComparison.OrdinalIgnoreCase))
            {
                await session.Writer.WriteLineAsync("501 Unknown COMPRESS algorithm", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync("206 Compression active", cancellationToken).ConfigureAwait(false);
            await activateCompression(cancellationToken).ConfigureAwait(false);
            session.State.IsCompressionActive = true;
            return true;
        }
    }
}
