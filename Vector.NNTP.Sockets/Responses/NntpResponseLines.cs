// <copyright file="NntpResponseLines.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: canonical response line text without CRLF (writer appends CRLF).

namespace Vector.NNTP.Sockets.Responses
{
    /// <summary>
    /// Canonical NNTP single-line response text (without trailing CRLF).
    /// </summary>
    internal static class NntpResponseLines
    {
        internal const string AuthenticationRequired480 = "480 Authentication required";
        internal const string TooManySessions481 = "481 Too many sessions";
        internal const string TooManySourceAddresses481 = "481 Too many source addresses";
        internal const string AuthenticationFailed481 = "481 Authentication failed";
        internal const string PermissionDenied502 = "502 Permission denied";
        internal const string ProgramFault503 = "503 Program fault, closing connection";
        internal const string TlsRequired483 = "483 Encryption or stronger authentication required";
        internal const string AlreadyAuthenticated502 = "502 Already authenticated";
        internal const string CompressionActive502 = "502 Compression active; command not permitted";
        internal const string StartTlsAfterCompress502 = "502 STARTTLS not permitted after COMPRESS";
        internal const string AuthAfterTlsRequired483 = "483 Encryption required for authentication";
    }
}
