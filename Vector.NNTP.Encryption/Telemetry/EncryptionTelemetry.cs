// <copyright file="EncryptionTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;

namespace Vector.NNTP.Encryption.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for ACME and renewal operations.
    /// </summary>
    internal static class EncryptionTelemetry
    {
        /// <summary>
        /// Activity source name for host SDK registration.
        /// </summary>
        internal const string SourceName = "Vector.NNTP.Encryption";

        /// <summary>
        /// Shared activity source for certificate renewal and issuance.
        /// </summary>
        internal static ActivitySource ActivitySource { get; } = new(SourceName, Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion);
    }
}
