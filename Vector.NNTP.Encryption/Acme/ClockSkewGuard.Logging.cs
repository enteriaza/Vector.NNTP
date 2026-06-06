// <copyright file="ClockSkewGuard.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ClockSkewGuard.Logging.cs -- Source-generated [LoggerMessage] static partial methods.

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="ClockSkewGuard"/>.
    /// </summary>
    internal static partial class ClockSkewGuard
    {
        /// <summary>
        /// Logs that local clock skew exceeds the configured ACME maximum before throwing.
        /// </summary>
        /// <param name="logger">Logger for ACME clock skew diagnostics.</param>
        /// <param name="skew">Observed absolute skew.</param>
        /// <param name="maxSkew">Configured maximum tolerated skew.</param>
        [LoggerMessage(EventId = 450, Level = LogLevel.Error,
            Message = "Certificates: System clock skew ({Skew}) exceeds configured maximum ({MaxSkew}); synchronize time (NTP) before using ACME")]
        internal static partial void LogClockSkewExceeded(ILogger logger, TimeSpan skew, TimeSpan maxSkew);
    }
}
