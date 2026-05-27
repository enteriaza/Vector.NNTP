// <copyright file="HdrParameterSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HDR/XHDR header field name validation (placeholder).

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Validates header field names supplied to HDR and XHDR commands.
    /// </summary>
    /// <remarks>
    /// Placeholder validation: non-empty names without whitespace. Full RFC 5536 field-name rules may be added later.
    /// </remarks>
    internal static class HdrParameterSyntax
    {
        /// <summary>
        /// Determines whether <paramref name="headerField"/> is acceptable for HDR/XHDR.
        /// </summary>
        /// <param name="headerField">Header field name without trailing colon.</param>
        /// <returns><see langword="true"/> when the field name is non-empty and contains no whitespace.</returns>
        internal static bool IsValid(string headerField)
        {
            return !string.IsNullOrWhiteSpace(headerField) && !headerField.AsSpan().ContainsAny(" \t\r\n:");
        }
    }
}
