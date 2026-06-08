// <copyright file="NntpServerIdentityExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: server FQDN helpers for SpamAssassin scan header synthesis.

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Identity helpers for <see cref="NntpServerOptions"/> used when synthesizing spamd scan headers.
    /// </summary>
    public static class NntpServerIdentityExtensions
    {
        /// <summary>
        /// Returns the server FQDN used in synthetic <c>Received:</c> <c>by</c> clauses.
        /// </summary>
        /// <param name="options">Bound server options.</param>
        /// <returns><c>{NodeName}.{DomainName}</c> when <see cref="NntpServerOptions.DomainName"/> is set; otherwise <see cref="NntpServerOptions.NodeName"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public static string GetServerFqdn(this NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(options.DomainName))
            {
                return options.NodeName;
            }

            return $"{options.NodeName}.{options.DomainName.Trim()}";
        }

        /// <summary>
        /// Returns the <c>by</c> clause value for synthetic <c>Received:</c> headers, combining the server FQDN with
        /// an optional software identification token.
        /// </summary>
        /// <param name="options">Bound server options.</param>
        /// <returns>
        /// <c>{fqdn} ({ServerIdentification})</c> when <see cref="NntpServerOptions.ServerIdentification"/> is
        /// non-empty after trimming; otherwise just <c>{fqdn}</c>.
        /// </returns>
        /// <remarks>
        /// The parenthesized product name is the same identification string used in the initial NNTP greeting and
        /// <c>CAPABILITIES IMPLEMENTATION</c> responses, allowing operators to distinguish server software in
        /// mail-tracing tools that parse <c>Received:</c> lines.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public static string GetServerReceivedByClause(this NntpServerOptions options)
        {
            string fqdn = options.GetServerFqdn();
            string ident = options.ServerIdentification?.Trim() ?? string.Empty;
            return ident.Length > 0 ? $"{fqdn} ({ident})" : fqdn;
        }

        /// <summary>
        /// Returns the synthetic <c>To:</c> address for spamd scan articles.
        /// </summary>
        /// <param name="options">Bound server options.</param>
        /// <returns><c>usenet@{GetServerFqdn(options)}</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public static string GetSpamScanToAddress(this NntpServerOptions options)
        {
            return $"usenet@{options.GetServerFqdn()}";
        }
    }
}
