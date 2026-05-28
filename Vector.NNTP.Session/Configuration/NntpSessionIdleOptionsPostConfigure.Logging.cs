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
        [LoggerMessage(Level = LogLevel.Information, Message = "NntpServer idleTimeoutSeconds={Seconds} takes precedence over IdleTimeout duration.")]
        public static partial void IdleTimeoutSecondsPrecedence(ILogger logger, int seconds);
    }
}
