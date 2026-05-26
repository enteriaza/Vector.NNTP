// <copyright file="CredentialPlaceholderDetector.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CredentialPlaceholderDetector.cs -- Detects template placeholder credentials in configuration strings.

using System.Collections.Frozen;

namespace Vector.NNTP.Utilities.Validation;

/// <summary>
/// Detects template placeholder credentials in configuration strings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Options validators can reject changeme-style placeholder credentials without duplicating the
/// placeholder list or lookup logic.</para>
/// </remarks>
public static class CredentialPlaceholderDetector
{
    /// <summary>
    /// Shared placeholder tokens rejected during options validation.
    /// </summary>
    public static readonly FrozenSet<string> CommonPlaceholders = FrozenSet.ToFrozenSet(
        ["changeme", "password", "your-password-here", "replace-me", "todo", "fixme"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is empty, whitespace, or matches a known placeholder.
    /// </summary>
    /// <param name="value">Credential string from configuration binding.</param>
    /// <param name="additionalPlaceholders">Optional section-specific placeholders merged with <see cref="CommonPlaceholders"/>.</param>
    /// <returns><see langword="true"/> when the value must be rejected as a placeholder.</returns>
    public static bool IsPlaceholder(string? value, FrozenSet<string>? additionalPlaceholders = null)
    {
        return string.IsNullOrWhiteSpace(value)
            || CommonPlaceholders.Contains(value)
            || additionalPlaceholders?.Contains(value) == true;
    }
}
