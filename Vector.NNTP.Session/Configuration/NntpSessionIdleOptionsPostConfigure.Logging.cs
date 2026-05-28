// <copyright file="NntpSessionIdleOptionsPostConfigure.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Configuration
{
    /// <summary>
    /// Logging for idle timeout post-configure.
    /// </summary>
    internal static partial class NntpSessionIdleOptionsPostConfigureLog
    {
        /// <summary>
        /// Log an information message when the idle timeout seconds take precedence over the idle timeout duration.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="seconds">The seconds.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "NntpServer idleTimeoutSeconds={Seconds} takes precedence over IdleTimeout duration.")]
        public static partial void IdleTimeoutSecondsPrecedence(ILogger logger, int seconds);
    }
}
