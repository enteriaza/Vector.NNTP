// <copyright file="CachingMySqlUserRecordStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="CachingMySqlUserRecordStore"/>.
    /// </summary>
    /// <remarks>
    /// Cold-path logging for post-success authentication cache hits and misses on SASL warm paths.
    /// </remarks>
    internal sealed partial class CachingMySqlUserRecordStore
    {
        /// <summary>
        /// Logs an authentication cache hit.
        /// </summary>
        /// <param name="logger">Logger for authentication cache diagnostics.</param>
        /// <param name="accountName">Account name.</param>
        [LoggerMessage(
            EventId = 420,
            Level = LogLevel.Debug,
            Message = "Auth.MySql auth cache hit for '{AccountName}'")]
        private static partial void AuthCacheHit(ILogger logger, string accountName);

        /// <summary>
        /// Logs an authentication cache miss.
        /// </summary>
        /// <param name="logger">Logger for authentication cache diagnostics.</param>
        /// <param name="accountName">Account name.</param>
        [LoggerMessage(
            EventId = 421,
            Level = LogLevel.Debug,
            Message = "Auth.MySql auth cache miss for '{AccountName}'")]
        private static partial void AuthCacheMiss(ILogger logger, string accountName);
    }
}
