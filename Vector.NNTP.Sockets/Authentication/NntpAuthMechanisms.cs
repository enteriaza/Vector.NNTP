// <copyright file="NntpAuthMechanisms.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: canonical NNTP authentication mechanism labels for host audit logging.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Canonical mechanism names passed to host authentication implementations for consistent audit logging.
    /// </summary>
    public static class NntpAuthMechanisms
    {
        /// <summary>
        /// AUTHINFO USER followed by AUTHINFO PASS.
        /// </summary>
        public const string AuthInfoUserPass = "AUTHINFO USER/PASS";

        /// <summary>
        /// SASL PLAIN (RFC 4616).
        /// </summary>
        public const string SaslPlain = "SASL PLAIN";

        /// <summary>
        /// SASL LOGIN.
        /// </summary>
        public const string SaslLogin = "SASL LOGIN";

        /// <summary>
        /// SASL CRAM-MD5.
        /// </summary>
        public const string SaslCramMd5 = "SASL CRAM-MD5";

        /// <summary>
        /// SASL SCRAM-SHA-256 (RFC 7677).
        /// </summary>
        public const string SaslScramSha256 = "SASL SCRAM-SHA-256";
    }
}
