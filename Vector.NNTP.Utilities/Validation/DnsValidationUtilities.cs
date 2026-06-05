// <copyright file="DnsValidationUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// DnsValidationUtilities.cs -- DNS resolution helpers for host validation at startup.
//
// Allocation characteristics:
//   - Error strings allocate on failure paths only.
//
// Thread safety:
//   All methods are static and stateless. Blocking DNS calls must not run on hot paths.

using System.Net;

namespace Vector.NNTP.Utilities.Validation
{
    /// <summary>
    /// DNS resolution helpers for options validation.
    /// </summary>
    /// <remarks>
    /// <para><b>Blocking:</b> Uses <see cref="M:System.Net.Dns.GetHostEntry(System.String)"/> for non-literal hosts. Invoke only from startup
    /// validation, not hot paths.</para>
    ///
    /// <para><b>Exception policy:</b> <see cref="TryValidateHost"/> never throws for resolution failures; errors are returned via
    /// the <c>out string? error</c> parameter.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.</para>
    /// </remarks>
    public static class DnsValidationUtilities
    {
        /// <summary>
        /// Attempts to validate that <paramref name="host"/> is an IP literal or resolves to at least one address.
        /// </summary>
        /// <param name="host">Bare hostname or IP literal (no port suffix).</param>
        /// <param name="error">Human-readable error when validation fails; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the host is acceptable.</returns>
        public static bool TryValidateHost(string host, out string? error)
        {
            if (IPAddress.TryParse(host, out _))
            {
                error = null;
                return true;
            }

            try
            {
                IPHostEntry entry = System.Net.Dns.GetHostEntry(host);
                if (entry.AddressList.Length == 0)
                {
                    error = $"Host '{host}' did not resolve to any IP addresses.";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Host '{host}' DNS resolution failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Validates that <paramref name="host"/> is an IP literal or resolves to at least one address.
        /// </summary>
        /// <param name="host">Bare hostname or IP literal (no port suffix).</param>
        /// <param name="error">Human-readable error when validation fails; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the host is acceptable.</returns>
        /// <remarks>
        /// <para>Prefer <see cref="TryValidateHost"/> for new call sites. This method forwards to the same implementation.</para>
        /// </remarks>
        public static bool ValidateHost(string host, out string? error)
        {
            return TryValidateHost(host, out error);
        }
    }
}
