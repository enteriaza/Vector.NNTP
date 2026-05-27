// <copyright file="FakeNntpTransitStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory transit storage for RFC 4644 tests.

using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// In-memory transit storage for CHECK/IHAVE/TAKETHIS tests.
    /// </summary>
    internal sealed class FakeNntpTransitStorage : INntpTransitStorage
    {
        private readonly HashSet<string> _have = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(!this._have.Contains(messageId));
        }

        /// <inheritdoc />
        public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(!this._have.Contains(messageId));
        }

        /// <inheritdoc />
        public ValueTask<bool> TakeThisAsync(string messageId, ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
        {
            _ = articleBytes;
            _ = cancellationToken;
            this._have.Add(messageId);
            return ValueTask.FromResult(true);
        }
    }
}
