// <copyright file="DnsValidationUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// DnsValidationUtilities.cs -- DNS resolution helpers for host validation at startup.

using System.Net;

namespace Vector.NNTP.Utilities.Validation;

/// <summary>
/// DNS resolution helpers for options validation.
/// </summary>
/// <remarks>
/// <para><b>Blocking:</b> Uses <see cref="System.Net.Dns.GetHostEntry(string)"/> for non-literal hosts. Invoke only from startup
/// validation, not hot paths.</para>
/// </remarks>
public static class DnsValidationUtilities
{
    /// <summary>
    /// Validates that <paramref name="host"/> is an IP literal or resolves to at least one address.
    /// </summary>
    /// <param name="host">Bare hostname or IP literal (no port suffix).</param>
    /// <param name="error">Human-readable error when validation fails; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the host is acceptable.</returns>
    public static bool ValidateHost(string host, out string? error)
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
}
