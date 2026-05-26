// <copyright file="CredentialRedactionUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CredentialRedactionUtilities.cs -- Credential redaction helpers for log-safe configuration output.

using System.Text.RegularExpressions;

namespace Vector.NNTP.Utilities.Security;

/// <summary>
/// Credential redaction helpers for sanitising connection strings and token-like secrets before logging.
/// </summary>
public static partial class CredentialRedactionUtilities
{
    private const int MinPartialRevealLength = 12;
    private const int RevealPrefix = 4;
    private const int RevealSuffix = 4;
    private const string MaskPlaceholder = "****";

    /// <summary>
    /// Redacts <c>password=...</c> segments in comma-delimited connection strings (e.g. StackExchange.Redis).
    /// </summary>
    /// <param name="value">Connection string value.</param>
    /// <returns>Redacted value.</returns>
    public static string RedactPassword(string value) => PasswordCommaDelimitedRegex().Replace(value, "password=***");

    /// <summary>
    /// Redacts <c>Password=...</c> and <c>Pwd=...</c> segments in semicolon-delimited connection strings (ADO.NET style).
    /// </summary>
    /// <param name="value">Connection string value.</param>
    /// <returns>Redacted value.</returns>
    public static string RedactConnectionString(string value)
    {
        string redacted = PasswordSemicolonDelimitedRegex().Replace(value, "$1Password=***");
        return PwdSemicolonDelimitedRegex().Replace(redacted, "$1Pwd=***");
    }

    /// <summary>
    /// Masks an API token by revealing only the first and last four characters when long enough.
    /// </summary>
    /// <param name="token">Token value.</param>
    /// <returns>Redacted token.</returns>
    public static string RedactApiToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return MaskPlaceholder;
        }

        if (token.Length < MinPartialRevealLength)
        {
            return MaskPlaceholder;
        }

        return string.Concat(
            token.AsSpan(0, RevealPrefix),
            MaskPlaceholder,
            token.AsSpan(token.Length - RevealSuffix));
    }

    /// <summary>
    /// Applies all supported connection-string redaction patterns.
    /// </summary>
    /// <param name="value">Connection string.</param>
    /// <returns>Redacted value.</returns>
    public static string RedactAll(string value) => RedactConnectionString(RedactPassword(value));

    [GeneratedRegex(@"(?i)\bpassword\s*=\s*[^,]+", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordCommaDelimitedRegex();

    [GeneratedRegex(@"(?i)(^|;)\s*Password\s*=\s*[^;]+", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordSemicolonDelimitedRegex();

    [GeneratedRegex(@"(?i)(^|;)\s*Pwd\s*=\s*[^;]+", RegexOptions.CultureInvariant)]
    private static partial Regex PwdSemicolonDelimitedRegex();
}
