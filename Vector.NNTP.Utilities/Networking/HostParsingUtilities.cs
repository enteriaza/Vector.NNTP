// <copyright file="HostParsingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HostParsingUtilities.cs -- Shared host string parsing helpers for configuration validation.

using System.Net;
using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Networking;

/// <summary>
/// Host string parsing helpers for configuration validation: port suffix detection, URI scheme detection, and IPv6
/// bracket stripping.
/// </summary>
public static class HostParsingUtilities
{
    /// <summary>
    /// Returns <see langword="true"/> when a host string appears to contain a <c>":port"</c> suffix.
    /// </summary>
    /// <param name="host">Host string to inspect.</param>
    /// <returns><see langword="true"/> if the host appears to contain a port suffix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasPortSuffix(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out _))
        {
            return false;
        }

        int lastColon = host.LastIndexOf(':');
        if (lastColon < 0 || lastColon == host.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> afterColon = host.AsSpan(lastColon + 1);
        foreach (char c in afterColon)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a host string contains a URI scheme separator (<c>"://"</c>).
    /// </summary>
    /// <param name="host">Host string to inspect.</param>
    /// <returns><see langword="true"/> if a scheme separator is present.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasUriScheme(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        return host.Contains("://", StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips RFC 3986 IPv6 literal brackets (<c>"[::1]"</c> -&gt; <c>"::1"</c>). Returns the input unchanged for
    /// non-bracketed inputs.
    /// </summary>
    /// <param name="host">Host string.</param>
    /// <returns>Unwrapped host string, or <see langword="null"/> if input is <see langword="null"/>.</returns>
    public static string? StripIPv6Brackets(string? host)
    {
        if (host is null)
        {
            return null;
        }

        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            return host[1..^1];
        }

        return host;
    }
}
