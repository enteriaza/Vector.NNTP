// <copyright file="NntpPipeTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory pipe transport for protocol unit tests.

using System.Security.Cryptography.X509Certificates;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Pipe-backed session transport for golden transcript tests (no TCP socket).
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpPipeTransport"/> class.
    /// </remarks>
    /// <param name="input">Server-side input reader (client writes here).</param>
    /// <param name="output">Server-side output writer (client reads here).</param>
    internal sealed class NntpPipeTransport(PipeReader input, PipeWriter output) : INntpSessionTransport
    {
        private readonly PipeWriter _output = output ?? throw new ArgumentNullException(nameof(output));
        private readonly PipeReader _input = input;

        /// <inheritdoc />
        public PipeReader Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

        /// <inheritdoc />
        public PipeWriter Output { get; } = output;

        /// <inheritdoc />
        Task INntpSessionTransport.UpgradeToTlsAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken)
        {
            _ = serverCertificate;
            _ = cancellationToken;
            throw new NotSupportedException("STARTTLS is not supported on in-memory pipe transports.");
        }

        /// <inheritdoc />
        ValueTask INntpSessionTransport.ActivateDeflateCompressionAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException("COMPRESS is not supported on in-memory pipe transports.");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _output.CompleteAsync().ConfigureAwait(false);
            await _input.CompleteAsync().ConfigureAwait(false);
        }
    }
}
