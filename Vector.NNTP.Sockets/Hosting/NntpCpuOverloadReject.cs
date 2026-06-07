// <copyright file="NntpCpuOverloadReject.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: RFC 3977 400 service unavailable response before connection teardown.

using Vector.NNTP.Sockets.Responses;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Writes the RFC 3977 <c>400 Service temporarily unavailable</c> greeting and flushes the transport.
    /// </summary>
    /// <remarks>
    /// Callers must dispose the socket or transport immediately after this helper returns.
    /// </remarks>
    internal static class NntpCpuOverloadReject
    {
        /// <summary>
        /// Writes the pre-encoded 400 response to the stream and flushes.
        /// </summary>
        /// <param name="stream">Connected transport stream (cleartext or TLS).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes after flush.</returns>
        internal static async ValueTask WriteAndFlushAsync(Stream stream, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ReadOnlyMemory<byte> payload = NntpPreencodedResponses.ServiceUnavailable400;
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
