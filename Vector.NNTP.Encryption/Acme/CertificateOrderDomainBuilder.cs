// <copyright file="CertificateOrderDomainBuilder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Maps <see cref="CertificateOrderMode"/> and configured domains to the ACME identifier set for a single order.
    /// </summary>
    internal static class CertificateOrderDomainBuilder
    {
        private const int MaxLabelLength = 63;

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

        private static string NormalizeDomain(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('.');

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

                if (!DnsAsciiEncoding.IsAscii(name.AsSpan()))
                {
                    throw new InvalidOperationException($"Invalid DNS identifier '{raw}' (non-ASCII characters are not supported).");
                }

                if (name[0] == '.' || name[^1] == '.' || name.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Invalid DNS identifier '{raw}' (leading dot, trailing dot, or empty label).");
                }

                string[] labels = name.Split('.');
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i].Length == 0)
                    {
                        throw new InvalidOperationException($"Invalid DNS identifier '{raw}' (empty label).");
                    }

                    if (labels[i].Length > MaxLabelLength)
                    {
                        throw new InvalidOperationException($"Invalid DNS identifier '{raw}' (label exceeds {MaxLabelLength} characters).");
                    }
                }
            }
        }

        private static string[] ValidateWildcardOnly(string[] trimmed)
        {
            if (trimmed.Length != 1)
            {
                throw new InvalidOperationException("WildcardOnly order mode requires exactly one domain entry (for example *.example.com).");
            }

            if (!trimmed[0].StartsWith("*.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("WildcardOnly order mode requires a leading '*.'.");
            }

            return trimmed;
        }

        private static string[] ValidateWildcardAndHostname(string[] trimmed)
        {
            if (trimmed.Length != 2)
            {
                throw new InvalidOperationException("WildcardAndHostname order mode requires exactly two domain entries: wildcard and explicit hostname.");
            }

            if (!trimmed[0].StartsWith("*.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("WildcardAndHostname order mode expects the wildcard as the first domain entry.");
            }

            if (trimmed[1].StartsWith("*.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("WildcardAndHostname order mode expects the second entry to be a non-wildcard hostname.");
            }

            return trimmed;
        }

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
