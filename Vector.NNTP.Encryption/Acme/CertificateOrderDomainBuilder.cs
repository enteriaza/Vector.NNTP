// <copyright file="CertificateOrderDomainBuilder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Utilities.Dns;

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Maps <see cref="CertificateOrderMode"/> and configured domains to the ACME identifier set for a single order.
    /// </summary>
    internal static class CertificateOrderDomainBuilder
    {
        /// <summary>
        /// Builds the ordered list of DNS identifiers for the ACME <c>newOrder</c> request.
        /// </summary>
        /// <param name="options">Let's Encrypt options.</param>
        /// <returns>Domain names for the ACME order.</returns>
        /// <exception cref="InvalidOperationException">When the domain set does not match the selected order mode.</exception>
        public static string[] BuildOrderDomains(LetsEncryptOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            string[] domains = options.DomainNames ?? [];
            List<string> trimmedList = [];
            foreach (string d in domains)
            {
                string t = NormalizeDomain(d);
                if (t.Length > 0)
                {
                    trimmedList.Add(t);
                }
            }

            string[] trimmed = [.. trimmedList];
            ValidateNoDuplicates(trimmed);
            ValidateDnsSyntax(trimmed);

            return options.OrderMode switch
            {
                CertificateOrderMode.WildcardOnly => ValidateWildcardOnly(trimmed),
                CertificateOrderMode.WildcardAndHostname => ValidateWildcardAndHostname(trimmed),
                CertificateOrderMode.SingleHostname => ValidateSingleHostname(trimmed),
                _ => throw new InvalidOperationException($"Unsupported certificate order mode '{options.OrderMode}'."),
            };
        }

        /// <summary>
        /// Normalizes a domain string by trimming whitespace and removing any trailing periods.
        /// </summary>
        /// <param name="value">The domain string to normalize.</param>
        /// <returns>A normalized domain string, or an empty string if the input is null or consists only of whitespace.</returns>
        private static string NormalizeDomain(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('.');
        }

        /// <summary>
        /// Validates that the provided array of domain identifiers does not contain duplicates.
        /// </summary>
        /// <param name="trimmed">An array of domain identifiers to validate for duplicates.</param>
        /// <exception cref="InvalidOperationException">Thrown when a duplicate domain identifier is found in the array.</exception>
        private static void ValidateNoDuplicates(string[] trimmed)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string d in trimmed)
            {
                if (!seen.Add(d))
                {
                    throw new InvalidOperationException($"Duplicate domain identifier '{d}' in LetsEncrypt:DomainNames.");
                }
            }
        }

        /// <summary>
        /// Validates that each DNS identifier in the array conforms to DNS syntax rules, including ASCII encoding,
        /// label length, and proper wildcard usage.
        /// </summary>
        /// <param name="trimmed">An array of DNS identifiers to validate.</param>
        /// <exception cref="InvalidOperationException">Thrown when a DNS identifier is invalid due to non-ASCII characters, improper wildcard usage, leading or
        /// trailing dots, empty labels, or labels exceeding the maximum length.</exception>
        private static void ValidateDnsSyntax(string[] trimmed)
        {
            foreach (string raw in trimmed)
            {
                bool wildcard = raw.StartsWith("*.", StringComparison.Ordinal);
                string name = wildcard ? raw[2..] : raw;
                if (name.Length == 0)
                {
                    throw new InvalidOperationException($"Invalid DNS identifier '{raw}' (missing name after '*.' wildcard prefix).");
                }

                if (!DnsWireFormatUtilities.TryValidateDnsName(name, out string? error))
                {
                    throw new InvalidOperationException($"Invalid DNS identifier '{raw}' ({error}).");
                }
            }
        }

        /// <summary>
        /// Validates that the input array contains exactly one domain entry with a leading '*.' for wildcard
        /// processing.
        /// </summary>
        /// <param name="trimmed">An array of trimmed domain entries to validate.</param>
        /// <returns>The validated array of trimmed domain entries.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the input array does not contain exactly one entry or the entry does not start with '*.'.</exception>
        private static string[] ValidateWildcardOnly(string[] trimmed)
        {
            return trimmed.Length != 1
                ? throw new InvalidOperationException("WildcardOnly order mode requires exactly one domain entry (for example *.example.com).")
                : !trimmed[0].StartsWith("*.", StringComparison.Ordinal)
                ? throw new InvalidOperationException("WildcardOnly order mode requires a leading '*.'.")
                : trimmed;
        }

        /// <summary>
        /// Validates that the input array contains exactly two domain entries: a wildcard and an explicit hostname, in
        /// the correct order.
        /// </summary>
        /// <param name="trimmed">An array containing two domain entries: the first as a wildcard and the second as an explicit hostname.</param>
        /// <returns>The validated array of domain entries.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the array does not contain exactly two entries, the first entry is not a wildcard, or the second
        /// entry is a wildcard.</exception>
        private static string[] ValidateWildcardAndHostname(string[] trimmed)
        {
            return trimmed.Length != 2
                ? throw new InvalidOperationException("WildcardAndHostname order mode requires exactly two domain entries: wildcard and explicit hostname.")
                : !trimmed[0].StartsWith("*.", StringComparison.Ordinal)
                ? throw new InvalidOperationException("WildcardAndHostname order mode expects the wildcard as the first domain entry.")
                : trimmed[1].StartsWith("*.", StringComparison.Ordinal)
                ? throw new InvalidOperationException("WildcardAndHostname order mode expects the second entry to be a non-wildcard hostname.")
                : trimmed;
        }

        /// <summary>
        /// Validates that the array contains at least one hostname and does not include wildcard identifiers for single
        /// hostname order mode.
        /// </summary>
        /// <param name="trimmed">An array of trimmed hostname strings to validate.</param>
        /// <returns>The validated array of hostnames.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the array is empty or contains wildcard identifiers.</exception>
        private static string[] ValidateSingleHostname(string[] trimmed)
        {
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException("SingleHostname order mode requires at least one domain entry.");
            }

            foreach (string d in trimmed)
            {
                if (d.StartsWith("*.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("SingleHostname order mode must not include wildcard identifiers; use WildcardOnly or WildcardAndHostname.");
                }
            }

            return trimmed;
        }
    }
}
