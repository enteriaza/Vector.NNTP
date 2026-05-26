// <copyright file="FormattingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// FormattingUtilities.cs -- Allocation-conscious formatting helpers for human-readable log output.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.Utilities.Diagnostics;

/// <summary>
/// Allocation-conscious formatting helpers for human-readable log output.
/// </summary>
/// <remarks>
/// <para><b>Design goal:</b> Provide small, dependency-free formatting helpers that are safe to call from logging paths
/// without causing large allocations or leaking sensitive values.</para>
/// </remarks>
public static class FormattingUtilities
{
    /// <summary>
    /// Number of bytes per gibibyte (GiB) as a <see cref="double"/>.
    /// </summary>
    public const double BytesPerGiB = 1_024.0 * 1_024 * 1_024;

    /// <summary>
    /// Default maximum byte length used by <see cref="FormatObjectValue"/> when the caller does not specify a cap.
    /// </summary>
    public const int DefaultMaxByteLength = 256;

    /// <summary>
    /// Converts a byte count to gibibytes for human-readable log output.
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>GiB value as a <see cref="double"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToGiB(long bytes) => bytes / BytesPerGiB;

    /// <summary>
    /// Normalises an <see cref="IPAddress"/> by converting IPv4-mapped IPv6 addresses (<c>::ffff:a.b.c.d</c>) to their
    /// IPv4 form.
    /// </summary>
    /// <param name="address">Address to normalise.</param>
    /// <returns>The normalised address.</returns>
    public static IPAddress NormaliseAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    /// <summary>
    /// Formats a host array and port into a human-readable summary string.
    /// </summary>
    /// <param name="hosts">Hostnames or IP literals.</param>
    /// <param name="port">Port number.</param>
    /// <returns>A string such as <c>"host1:5672, host2:5672"</c>.</returns>
    public static string FormatEndpointSummary(string[] hosts, int port)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        if (hosts.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new(capacity: hosts.Length * 24);

        for (int i = 0; i < hosts.Length; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }

            sb.Append(hosts[i]);
            sb.Append(':');
            sb.Append(port);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats key-value pairs into <c>"k=v, k2=v2"</c> text for diagnostic logging.
    /// </summary>
    /// <param name="pairs">Pairs to format.</param>
    /// <returns>Formatted string.</returns>
    public static string FormatKeyValuePairs(IReadOnlyDictionary<string, object> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new(capacity: pairs.Count * 24);
        bool first = true;

        foreach (KeyValuePair<string, object> kvp in pairs)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(FormatObjectValue(kvp.Value, DefaultMaxByteLength));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats key-value pairs into <c>"k=v, k2=v2"</c> text for diagnostic logging.
    /// </summary>
    /// <param name="pairs">Pairs to format.</param>
    /// <returns>Formatted string.</returns>
    public static string FormatKeyValuePairs(IDictionary<string, object> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new(capacity: pairs.Count * 24);
        bool first = true;

        foreach (KeyValuePair<string, object> kvp in pairs)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(FormatObjectValue(kvp.Value, DefaultMaxByteLength));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats an arbitrary object value for diagnostic logging.
    /// </summary>
    /// <param name="value">Value to format.</param>
    /// <param name="maxByteLength">Maximum byte length when decoding <see cref="byte"/> arrays as UTF-8.</param>
    /// <returns>A safe string representation.</returns>
    public static string FormatObjectValue(object? value, int maxByteLength = DefaultMaxByteLength)
    {
        if (value is null)
        {
            return "<null>";
        }

        if (value is byte[] bytes)
        {
            return FormatByteArray(bytes, maxByteLength);
        }

        return value.ToString() ?? "<null>";
    }

    /// <summary>
    /// Formats a UTF-8 byte array for display with truncation.
    /// </summary>
    /// <param name="bytes">UTF-8 bytes.</param>
    /// <param name="maxByteLength">Maximum bytes to decode.</param>
    /// <returns>A quoted string when decoding succeeds; otherwise a length placeholder.</returns>
    private static string FormatByteArray(byte[] bytes, int maxByteLength)
    {
        if (bytes.Length == 0)
        {
            return "\"\"";
        }

        int length = Math.Min(bytes.Length, maxByteLength);

        try
        {
            string s = System.Text.Encoding.UTF8.GetString(bytes, 0, length);
            if (bytes.Length > length)
            {
                return $"\"{s}...\"";
            }

            return $"\"{s}\"";
        }
        catch (Exception)
        {
            return $"<{bytes.Length} bytes>";
        }
    }

    /// <summary>
    /// Throws when a byte count argument is negative.
    /// </summary>
    /// <param name="bytes">The offending byte count.</param>
    [DoesNotReturn]
    private static void ThrowBytesNegative(long bytes) =>
        throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count must be non-negative.");
}
