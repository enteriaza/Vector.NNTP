// <copyright file="NntpCredentialStoreTransientException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: signals SASL credential-store backend unavailability to the authentication service.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Indicates that a <see cref="ICramMd5CredentialStore"/> or <see cref="IScramCredentialStore"/> could not reach its
    /// backing database or otherwise failed transiently during credential lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Protocol mapping:</b> <see cref="NntpAuthenticationService"/> catches this type and responds with
    /// <c>503 Temporary authentication failure</c>. Implementors must not use it for invalid credentials, disabled
    /// accounts, or mechanism policy rejections — those remain <see langword="false"/> return values from the store
    /// contract.
    /// </para>
    /// <para>
    /// <b>Distinction:</b> Unlike <see cref="NntpAuthResult.TransientFailure"/> on <see cref="INntpCredentialValidator"/>,
    /// this exception surfaces on the synchronous SASL credential lookup path where no <see cref="ValueTask"/> result is
    /// available.
    /// </para>
    /// </remarks>
    public sealed class NntpCredentialStoreTransientException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCredentialStoreTransientException"/> class.
        /// </summary>
        /// <param name="message">Human-readable failure description.</param>
        public NntpCredentialStoreTransientException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCredentialStoreTransientException"/> class.
        /// </summary>
        /// <param name="message">Human-readable failure description.</param>
        /// <param name="innerException">Underlying database or I/O exception.</param>
        public NntpCredentialStoreTransientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
