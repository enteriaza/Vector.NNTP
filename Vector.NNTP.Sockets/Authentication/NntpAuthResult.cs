// <copyright file="NntpAuthResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: credential validation outcome DTO.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Outcome of credential validation for AUTHINFO or SASL password mechanisms.
    /// </summary>
    public readonly struct NntpAuthResult
    {
        private NntpAuthResult(NntpAuthStatus status, NntpSessionPolicy? policy)
        {
            Status = status;
            Policy = policy;
        }

        /// <summary>
        /// Gets the validation status.
        /// </summary>
        public NntpAuthStatus Status { get; }

        /// <summary>
        /// Gets the session policy when <see cref="Status"/> is <see cref="NntpAuthStatus.Success"/>.
        /// </summary>
        public NntpSessionPolicy? Policy { get; }

        /// <summary>
        /// Creates a successful result with the given policy.
        /// </summary>
        /// <param name="policy">Granted session policy.</param>
        /// <returns>Success result.</returns>
        public static NntpAuthResult Success(NntpSessionPolicy policy)
        {
            return new(NntpAuthStatus.Success, policy);
        }

        /// <summary>
        /// Creates an invalid-credentials result (481).
        /// </summary>
        /// <returns>Invalid credentials result.</returns>
        public static NntpAuthResult InvalidCredentials()
        {
            return new(NntpAuthStatus.InvalidCredentials, null);
        }

        /// <summary>
        /// Creates a transient backend failure result (503).
        /// </summary>
        /// <returns>Transient failure result.</returns>
        public static NntpAuthResult TransientFailure()
        {
            return new(NntpAuthStatus.TransientFailure, null);
        }
    }

    /// <summary>
    /// Credential validation status codes mapped to NNTP responses by the authentication service.
    /// </summary>
    public enum NntpAuthStatus
    {
        /// <summary>
        /// Authentication succeeded.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Invalid username or password (481).
        /// </summary>
        InvalidCredentials = 1,

        /// <summary>
        /// Backend or policy store unavailable (503).
        /// </summary>
        TransientFailure = 2,
    }
}
