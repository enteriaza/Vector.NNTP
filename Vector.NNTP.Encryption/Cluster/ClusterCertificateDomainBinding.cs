// <copyright file="ClusterCertificateDomainBinding.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Compares ACME order domains from configuration to domains carried on the cluster broadcast payload.
    /// </summary>
    internal static class ClusterCertificateDomainBinding
    {
        /// <summary>
        /// Returns true when <paramref name="payloadDomains"/> matches <paramref name="expectedOrderDomains"/> as a multiset (case-insensitive).
        /// </summary>
        /// <param name="expectedOrderDomains">Locally configured ACME order domains.</param>
        /// <param name="payloadDomains">Domains from the cluster payload.</param>
        /// <returns><see langword="true"/> when both sets match.</returns>
        public static bool OrderDomainsMatch(string[] expectedOrderDomains, string[]? payloadDomains)
        {
            ArgumentNullException.ThrowIfNull(expectedOrderDomains);
            if (payloadDomains is null || payloadDomains.Length == 0)
                return false;

            if (expectedOrderDomains.Length != payloadDomains.Length)
                return false;

            string[] a = new string[expectedOrderDomains.Length];
            string[] b = new string[payloadDomains.Length];
            for (int i = 0; i < expectedOrderDomains.Length; i++)
                a[i] = NormalizeDomain(expectedOrderDomains[i]);

            for (int i = 0; i < payloadDomains.Length; i++)
                b[i] = NormalizeDomain(payloadDomains[i]);

            Array.Sort(a, StringComparer.OrdinalIgnoreCase);
            Array.Sort(b, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Trims whitespace and trailing dots so multiset comparisons align with validator normalisation.
        /// </summary>
        /// <param name="value">Raw domain label from configuration or cluster payload.</param>
        /// <returns>Trimmed domain without trailing dot, or empty when null/whitespace.</returns>
        private static string NormalizeDomain(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('.');
        }
    }
}
