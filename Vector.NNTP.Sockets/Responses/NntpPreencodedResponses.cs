// <copyright file="NntpPreencodedResponses.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: pre-encoded ASCII NNTP responses with CRLF for common status codes.

namespace Vector.NNTP.Sockets.Responses
{
    /// <summary>
    /// Pre-encoded ASCII NNTP response payloads for hot dispatch paths.
    /// </summary>
    internal static class NntpPreencodedResponses
    {
        private static readonly Encoding Ascii = Encoding.ASCII;

        /// <summary>480 Authentication required.</summary>
        internal static ReadOnlyMemory<byte> AuthenticationRequired480 { get; } =
            Ascii.GetBytes(NntpResponseLines.AuthenticationRequired480 + "\r\n");

        /// <summary>502 Permission denied.</summary>
        internal static ReadOnlyMemory<byte> PermissionDenied502 { get; } =
            Ascii.GetBytes(NntpResponseLines.PermissionDenied502 + "\r\n");

        /// <summary>503 Program fault.</summary>
        internal static ReadOnlyMemory<byte> ProgramFault503 { get; } =
            Ascii.GetBytes(NntpResponseLines.ProgramFault503 + "\r\n");

        /// <summary>500 Unknown command.</summary>
        internal static ReadOnlyMemory<byte> UnknownCommand500 { get; } =
            Ascii.GetBytes("500 Unknown command\r\n");

        /// <summary>483 TLS required for auth.</summary>
        internal static ReadOnlyMemory<byte> TlsRequired483 { get; } =
            Ascii.GetBytes(NntpResponseLines.TlsRequired483 + "\r\n");

        /// <summary>502 Already authenticated.</summary>
        internal static ReadOnlyMemory<byte> AlreadyAuthenticated502 { get; } =
            Ascii.GetBytes(NntpResponseLines.AlreadyAuthenticated502 + "\r\n");
    }
}
