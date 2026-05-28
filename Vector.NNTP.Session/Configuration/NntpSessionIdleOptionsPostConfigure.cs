// <copyright file="NntpSessionIdleOptionsPostConfigure.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Session.Configuration
{
    /// <summary>
    /// Applies <c>idleTimeoutSeconds</c> precedence for session lease TTL sizing.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpSessionIdleOptionsPostConfigure"/> class.
    /// </remarks>
    /// <param name="logger">Logger.</param>
    public sealed class NntpSessionIdleOptionsPostConfigure(ILogger<NntpSessionIdleOptionsPostConfigure> logger) : IPostConfigureOptions<NntpSessionIdleOptions>
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<NntpSessionIdleOptionsPostConfigure> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Applies <c>idleTimeoutSeconds</c> precedence for session lease TTL sizing.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="options">The options.</param>
        public void PostConfigure(string? name, NntpSessionIdleOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.IdleTimeoutSeconds is int seconds && seconds > 0)
            {
                if (options.IdleTimeout > TimeSpan.Zero && options.IdleTimeout != TimeSpan.FromSeconds(seconds))
                {
                    NntpSessionIdleOptionsPostConfigureLog.IdleTimeoutSecondsPrecedence(_logger, seconds);
                }

                options.IdleTimeout = TimeSpan.FromSeconds(seconds);
            }
        }
    }
}
