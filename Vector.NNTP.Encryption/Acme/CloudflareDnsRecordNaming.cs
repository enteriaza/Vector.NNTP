// <copyright file="CloudflareDnsRecordNaming.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: maps ACME DNS-01 FQDNs to Cloudflare API record names.

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Normalizes ACME DNS-01 challenge hostnames for the Cloudflare DNS API.
    /// </summary>
    internal static class CloudflareDnsRecordNaming
    {
        /// <summary>
        /// Converts a challenge FQDN (for example <c>_acme-challenge.example.com</c>) to the name Cloudflare expects
        /// relative to the zone apex (for example <c>_acme-challenge</c>).
        /// </summary>
        /// <param name="fqdnRecordName">Fully-qualified challenge record name from ACME.</param>
        /// <param name="configuredDomainNames">Configured certificate domain identifiers.</param>
        /// <returns>Cloudflare <c>name</c> field value.</returns>
        internal static string NormalizeTxtRecordNameForApi(string fqdnRecordName, IReadOnlyList<string> configuredDomainNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fqdnRecordName);
            ArgumentNullException.ThrowIfNull(configuredDomainNames);

            string? zoneApex = TryGetZoneApex(configuredDomainNames);
            if (zoneApex is null)
            {
                return fqdnRecordName;
            }

            string suffix = "." + zoneApex;
            if (!fqdnRecordName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fqdnRecordName;
            }

            string relative = fqdnRecordName[..^suffix.Length];
            return relative.Length > 0 ? relative : "@";
        }

        private static string? TryGetZoneApex(IReadOnlyList<string> configuredDomainNames)
        {
            foreach (string domain in configuredDomainNames)
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    continue;
                }

                string normalized = domain.Trim().TrimEnd('.');
                if (normalized.StartsWith("*.", StringComparison.Ordinal))
                {
                    return normalized[2..];
                }

                if (normalized.Length > 0)
                {
                    return normalized;
                }
            }

            return null;
        }
    }
}
