// <copyright file="DeflateDuplexPipe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: COMPRESS DEFLATE marker type; full pipe wrapping deferred to integration slice.

namespace Vector.NNTP.Sockets.Compression
{
    /// <summary>
    /// Marks a session transport as DEFLATE-compressed per RFC 8054.
    /// </summary>
    /// <remarks>
    /// <para>Hosts replace the active <see cref="IDuplexPipe"/> with a deflate wrapper after <c>COMPRESS DEFLATE</c> succeeds.
    /// This type documents the integration point; wire-level deflate framing is applied in a future transport slice.</para>
    /// </remarks>
    public sealed class DeflateDuplexPipe : IDuplexPipe
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeflateDuplexPipe"/> class.
        /// </summary>
        /// <param name="inner">Inner transport pipe.</param>
        public DeflateDuplexPipe(IDuplexPipe inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            this.Input = inner.Input;
            this.Output = inner.Output;
        }

        /// <inheritdoc />
        public PipeReader Input { get; }

        /// <inheritdoc />
        public PipeWriter Output { get; }
    }
}
