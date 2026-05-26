// <copyright file="PostFilterContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterContext.cs -- Per-post client identity and timing supplied by the NNTP host.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Per-post client identity and timing supplied by the NNTP host (analogous to INN/nnrpd Perl globals and connection metadata).
    /// </summary>
    public sealed class PostFilterContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PostFilterContext"/> class.
        /// </summary>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="utcNow">Clock time for rate windows.</param>
        /// <param name="isAuthenticated">Whether the reader session is authenticated.</param>
        /// <param name="authenticatedUsername">Authenticated username when known; otherwise null.</param>
        /// <param name="clientReverseDomain">Second-level-ish domain from reverse DNS when available (Perl domain checks).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clientIp"/> is <see langword="null"/>.</exception>
        public PostFilterContext(
            IPAddress clientIp,
            DateTimeOffset utcNow,
            bool isAuthenticated,
            string? authenticatedUsername,
            string? clientReverseDomain)
        {
            this.ClientIp = clientIp ?? throw new ArgumentNullException(nameof(clientIp));
            this.UtcNow = utcNow;
            this.IsAuthenticated = isAuthenticated;
            this.AuthenticatedUsername = authenticatedUsername;
            this.ClientReverseDomain = clientReverseDomain;
        }

        /// <summary>Client IP address used for RBL/Tor checks, rate limiting, and optional client-token headers.</summary>
        public IPAddress ClientIp { get; }

        /// <summary>UTC instant used for sliding-window rate limits; inject a fixed clock in tests.</summary>
        public DateTimeOffset UtcNow { get; }

        /// <summary>When <see langword="true"/>, <see cref="AuthenticatedUsername"/> is authoritative for identity classification.</summary>
        public bool IsAuthenticated { get; }

        /// <summary>NNTP AUTH username when <see cref="IsAuthenticated"/> is <see langword="true"/>; otherwise <see langword="null"/>.</summary>
        public string? AuthenticatedUsername { get; }

        /// <summary>Second-level-ish reverse-DNS domain when the host resolved PTR (Perl-style domain checks); otherwise <see langword="null"/>.</summary>
        public string? ClientReverseDomain { get; }
    }
}

