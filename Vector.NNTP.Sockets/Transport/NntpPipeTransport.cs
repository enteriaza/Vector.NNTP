// <copyright file="NntpPipeTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory pipe transport for protocol unit tests.

namespace Vector.NNTP.Sockets.Transport
{
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Pipe-backed session transport for golden transcript tests (no TCP socket).
    /// </summary>
    internal sealed class NntpPipeTransport : INntpSessionTransport
    {
        private readonly PipeWriter _output;
        private readonly PipeReader _input;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpPipeTransport"/> class.
        /// </summary>
        /// <param name="input">Server-side input reader (client writes here).</param>
        /// <param name="output">Server-side output writer (client reads here).</param>
        public NntpPipeTransport(PipeReader input, PipeWriter output)
        {
            this.Input = input ?? throw new ArgumentNullException(nameof(input));
            this._output = output ?? throw new ArgumentNullException(nameof(output));
            this._input = input;
            this.Output = output;
        }

        /// <inheritdoc />
        public PipeReader Input { get; }

        /// <inheritdoc />
        public PipeWriter Output { get; }

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
            await this._output.CompleteAsync().ConfigureAwait(false);
            await this._input.CompleteAsync().ConfigureAwait(false);
        }
    }
}
