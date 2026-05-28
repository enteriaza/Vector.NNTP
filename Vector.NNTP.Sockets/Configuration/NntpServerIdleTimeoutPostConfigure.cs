// <copyright file="NntpServerIdleTimeoutPostConfigure.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Maps <c>NntpServer:idleTimeoutSeconds</c> onto <see cref="NntpServerOptions.IdleTimeout"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpServerIdleTimeoutPostConfigure"/> class.
    /// </remarks>
    /// <param name="logger">Logger.</param>
    public sealed class NntpServerIdleTimeoutPostConfigure(ILogger<NntpServerIdleTimeoutPostConfigure> logger) : IPostConfigureOptions<NntpServerOptions>
    {
        private readonly ILogger<NntpServerIdleTimeoutPostConfigure> logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public void PostConfigure(string? name, NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.IdleTimeoutSeconds is int seconds && seconds > 0)
            {
                if (options.IdleTimeout > TimeSpan.Zero && options.IdleTimeout != TimeSpan.FromSeconds(seconds))
                {
                    NntpServerIdleTimeoutPostConfigureLog.IdleTimeoutSecondsPrecedence(logger, seconds);
                }

                options.IdleTimeout = TimeSpan.FromSeconds(seconds);
            }
        }
    }
}
