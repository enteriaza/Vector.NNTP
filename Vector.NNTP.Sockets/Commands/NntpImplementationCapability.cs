// <copyright file="NntpImplementationCapability.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: IMPLEMENTATION capability line for CAPABILITIES responses.

namespace Vector.NNTP.Sockets.Commands
{
    using Vector.NNTP.Utilities.Diagnostics;

    /// <summary>
    /// Builds the RFC 3977 <c>IMPLEMENTATION</c> capability line from entry-assembly metadata.
    /// </summary>
    internal static class NntpImplementationCapability
    {
        private const string FallbackLine = "IMPLEMENTATION VectorNNTPD";

        /// <summary>
        /// Gets the <c>IMPLEMENTATION</c> capability line (without trailing CRLF).
        /// </summary>
        /// <returns>Capability line safe for NNTP ASCII output.</returns>
        internal static string GetLine()
        {
            try
            {
                string name = AssemblyInfoUtilities.ApplicationName;
                string version = AssemblyInfoUtilities.ApplicationVersion;
                return string.Equals(version, "0.0.0", StringComparison.Ordinal)
                    ? $"IMPLEMENTATION {name}"
                    : $"IMPLEMENTATION {name} v{version}";
            }
            catch
            {
                return FallbackLine;
            }
        }
    }
}
