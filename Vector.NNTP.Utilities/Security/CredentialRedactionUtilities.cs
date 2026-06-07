// <copyright file="CredentialRedactionUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// CredentialRedactionUtilities.cs -- Credential redaction helpers for log-safe configuration output.

using System.Text.RegularExpressions;

namespace Vector.NNTP.Utilities.Security
{
    /// <summary>
    /// Credential redaction helpers for sanitising connection strings and token-like secrets before logging.
    /// </summary>
    public static partial class CredentialRedactionUtilities
    {
        /// <summary>
        /// Minimum token length required before <see cref="RedactApiToken"/> reveals prefix and suffix characters.
        /// </summary>
        private const int MinPartialRevealLength = 12;

        /// <summary>
        /// Number of leading characters preserved when partially revealing a long API token.
        /// </summary>
        private const int RevealPrefix = 4;

        /// <summary>
        /// Number of trailing characters preserved when partially revealing a long API token.
        /// </summary>
        private const int RevealSuffix = 4;

        /// <summary>
        /// Mask placeholder substituted for short, null, or fully redacted token values.
        /// </summary>
        private const string MaskPlaceholder = "****";

        /// <summary>
        /// Redacts <c>password=...</c> segments in comma-delimited connection strings (e.g. StackExchange.Redis).
        /// </summary>
        /// <param name="value">Connection string value.</param>
        /// <returns>Connection string with comma-delimited <c>password=</c> values replaced by <c>password=***</c>.</returns>
        public static string RedactPassword(string value)
        {
            return PasswordCommaDelimitedRegex().Replace(value, "password=***");
        }

        /// <summary>
        /// Redacts <c>Password=...</c> and <c>Pwd=...</c> segments in semicolon-delimited connection strings (ADO.NET style).
        /// </summary>
        /// <param name="value">Connection string value.</param>
        /// <returns>Connection string with semicolon-delimited <c>Password=</c> and <c>Pwd=</c> values masked.</returns>
        public static string RedactConnectionString(string value)
        {
            string redacted = PasswordSemicolonDelimitedRegex().Replace(value, "$1Password=***");
            return PwdSemicolonDelimitedRegex().Replace(redacted, "$1Pwd=***");
        }

        /// <summary>
        /// Masks an API token by revealing only the first and last four characters when long enough.
        /// </summary>
        /// <param name="token">Token value.</param>
        /// <returns>
        /// <see cref="MaskPlaceholder"/> for null/short tokens; otherwise first and last four characters with a middle mask.
        /// </returns>
        public static string RedactApiToken(string? token)
        {
            return string.IsNullOrEmpty(token)
                ? MaskPlaceholder
                : token.Length < MinPartialRevealLength
                ? MaskPlaceholder
                : string.Concat(
                token.AsSpan(0, RevealPrefix),
                MaskPlaceholder,
                token.AsSpan(token.Length - RevealSuffix));
        }

        /// <summary>
        /// Applies all supported connection-string redaction patterns.
        /// </summary>
        /// <param name="value">Connection string.</param>
        /// <returns>Connection string after comma- and semicolon-delimited password redaction passes.</returns>
        public static string RedactAll(string value)
        {
            return RedactConnectionString(RedactPassword(value));
        }

        /// <summary>
        /// Source-generated regex matching comma-delimited <c>password=</c> segments (case-insensitive).
        /// </summary>
        [GeneratedRegex(@"(?i)\bpassword\s*=\s*[^,]+", RegexOptions.CultureInvariant)]
        private static partial Regex PasswordCommaDelimitedRegex();

        /// <summary>
        /// Source-generated regex matching semicolon-delimited <c>Password=</c> segments (ADO.NET style).
        /// </summary>
        [GeneratedRegex(@"(?i)(^|;)\s*Password\s*=\s*[^;]+", RegexOptions.CultureInvariant)]
        private static partial Regex PasswordSemicolonDelimitedRegex();

        /// <summary>
        /// Source-generated regex matching semicolon-delimited <c>Pwd=</c> segments (ADO.NET shorthand).
        /// </summary>
        [GeneratedRegex(@"(?i)(^|;)\s*Pwd\s*=\s*[^;]+", RegexOptions.CultureInvariant)]
        private static partial Regex PwdSemicolonDelimitedRegex();
    }
}
