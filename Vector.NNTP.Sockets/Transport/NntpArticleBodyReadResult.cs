// <copyright file="NntpArticleBodyReadResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Outcome of reading a dot-stuffed article body from the session pipe.
    /// </summary>
    internal enum NntpArticleBodyReadStatus
    {
        /// <summary>
        /// Body was read completely through the lone-dot terminator.
        /// </summary>
        Complete = 0,

        /// <summary>
        /// Decoded body size would exceed the configured maximum; the offending line was consumed.
        /// </summary>
        ExceededMaxSize,
    }

    /// <summary>
    /// Result of <see cref="NntpArticleBodyReader.ReadDotStuffedBodyAsync"/>.
    /// </summary>
    internal readonly struct NntpArticleBodyReadResult
    {
        private readonly byte[] _body;

        private NntpArticleBodyReadResult(NntpArticleBodyReadStatus status, byte[] body)
        {
            this.Status = status;
            this._body = body;
        }

        /// <summary>
        /// Gets how the read completed.
        /// </summary>
        internal NntpArticleBodyReadStatus Status { get; }

        /// <summary>
        /// Gets decoded body bytes when <see cref="Status"/> is <see cref="NntpArticleBodyReadStatus.Complete"/>.
        /// </summary>
        internal byte[] Body => this._body;

        /// <summary>
        /// Creates a successful read result.
        /// </summary>
        /// <param name="body">Decoded body bytes.</param>
        /// <returns>Complete read result.</returns>
        internal static NntpArticleBodyReadResult Complete(byte[] body) =>
            new(NntpArticleBodyReadStatus.Complete, body);

        /// <summary>
        /// Creates a result indicating the configured maximum article size was exceeded.
        /// </summary>
        /// <returns>Exceeded read result.</returns>
        internal static NntpArticleBodyReadResult ExceededMaxSize() =>
            new(NntpArticleBodyReadStatus.ExceededMaxSize, Array.Empty<byte>());
    }
}
