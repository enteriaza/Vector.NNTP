// <copyright file="DnsWireFormatUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// DnsWireFormatUtilities.cs -- DNS wire-format constants, validation, and QNAME encoding helpers (RFC 1035).

using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Utilities.Dns;

/// <summary>
/// DNS wire-format constants, validation, and QNAME encoding helpers per RFC 1035.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Centralises label and name length limits, validation, and dotted-name to QNAME encoding so
/// callers build DNS packets consistently and without unnecessary allocations.</para>
/// </remarks>
public static class DnsWireFormatUtilities
{
    /// <summary>
    /// Maximum length of a single DNS label in bytes (RFC 1035 §2.3.4).
    /// </summary>
    public const int MaxLabelLength = 63;

    /// <summary>
    /// Maximum DNS name length in wire format in bytes, including the trailing root label (RFC 1035 §3.1).
    /// </summary>
    public const int MaxWireNameLength = 255;

    /// <summary>
    /// Maximum presentation-form DNS hostname length (RFC 1035 §2.3.4).
    /// </summary>
    public const int MaxPresentationNameLength = 253;

    /// <summary>
    /// Fixed DNS header size in bytes (RFC 1035 §4.1.1).
    /// </summary>
    public const int DnsHeaderSize = 12;

    /// <summary>
    /// Size of QTYPE (2) + QCLASS (2) suffix appended after QNAME in the question section (RFC 1035 §4.1.2).
    /// </summary>
    public const int QuestionSuffixSize = 4;

    /// <summary>
    /// Maximum number of labels in a DNS name used for stack-allocated split buffers.
    /// </summary>
    public const int MaxLabelCount = 128;

    /// <summary>
    /// Validates a DNS name for wire-format encoding: non-empty, no empty labels, per-label length limits, total QNAME
    /// length, and ASCII-only content.
    /// </summary>
    /// <param name="name">DNS name in dotted form (e.g. <c>_acme-challenge.example.com</c>).</param>
    /// <param name="error">On failure, a descriptive error string; on success, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
    public static bool TryValidateDnsName(string name, out string? error)
    {
        if (string.IsNullOrEmpty(name))
        {
            error = "DNS name must not be null or empty.";
            return false;
        }

        ReadOnlySpan<char> nameSpan = name.AsSpan();

        Span<Range> labelRanges = stackalloc Range[MaxLabelCount];
        int labelCount = nameSpan.Split(labelRanges, '.', StringSplitOptions.None);

        if (labelCount == labelRanges.Length)
        {
            error = $"DNS name '{name}' contains too many labels ({labelCount}+).";
            return false;
        }

        int qnameLength = 1;

        for (int i = 0; i < labelCount; i++)
        {
            ReadOnlySpan<char> label = nameSpan[labelRanges[i]];

            if (label.IsEmpty)
            {
                error = $"DNS name '{name}' contains an empty label (consecutive dots, leading dot, or trailing dot).";
                return false;
            }

            if (label.Length > MaxLabelLength)
            {
                error = $"DNS label '{label.ToString()}' exceeds the maximum length of {MaxLabelLength} bytes (RFC 1035 §2.3.4).";
                return false;
            }

            if (!EncodingUtilities.IsAscii(label))
            {
                error = $"DNS label '{label.ToString()}' contains non-ASCII characters. DNS names must be ASCII-only (RFC 1035 §2.3.4).";
                return false;
            }

            qnameLength += 1 + label.Length;
        }

        if (qnameLength > MaxWireNameLength)
        {
            error = $"DNS name '{name}' exceeds the maximum QNAME length of {MaxWireNameLength} bytes (RFC 1035 §3.1).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Computes the wire-format QNAME length for a dotted DNS name, including the trailing root label.
    /// </summary>
    /// <param name="name">DNS name in dotted form (already validated).</param>
    /// <returns>Wire-format QNAME length in bytes.</returns>
    public static int ComputeWireNameLength(string name)
    {
        // name: "a.b.c" => (1+1) + (1+1) + (1+1) + 1 root label = 7.
        int length = 1;

        ReadOnlySpan<char> span = name.AsSpan();
        Span<Range> labelRanges = stackalloc Range[MaxLabelCount];
        int labelCount = span.Split(labelRanges, '.', StringSplitOptions.None);

        for (int i = 0; i < labelCount; i++)
        {
            ReadOnlySpan<char> label = span[labelRanges[i]];
            length += 1 + label.Length;
        }

        return length;
    }

    /// <summary>
    /// Encodes a dotted DNS name into QNAME wire format.
    /// </summary>
    /// <param name="name">DNS name in dotted form (already validated).</param>
    /// <param name="destination">Destination buffer.</param>
    /// <returns>Bytes written (QNAME length).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is too short.</exception>
    public static int EncodeDnsName(string name, Span<byte> destination)
    {
        ReadOnlySpan<char> span = name.AsSpan();
        Span<Range> labelRanges = stackalloc Range[MaxLabelCount];
        int labelCount = span.Split(labelRanges, '.', StringSplitOptions.None);

        int requiredLength = 1;
        for (int i = 0; i < labelCount; i++)
        {
            requiredLength += 1 + span[labelRanges[i]].Length;
        }

        if (destination.Length < requiredLength)
        {
            throw new ArgumentException($"Destination span is too short (required={requiredLength}, actual={destination.Length}).", nameof(destination));
        }

        int offset = 0;
        for (int i = 0; i < labelCount; i++)
        {
            ReadOnlySpan<char> label = span[labelRanges[i]];
            destination[offset++] = (byte)label.Length;
            offset += EncodingUtilities.AsciiToSpan(label, destination[offset..]);
        }

        destination[offset++] = 0;
        return offset;
    }
}
