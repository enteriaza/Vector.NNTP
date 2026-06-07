// <copyright file="DevelopmentNntpServiceStubs.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for development NNTP stubs.

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="DevelopmentNntpServiceStubs"/>.
    /// </summary>
    internal static partial class DevelopmentNntpServiceStubsLog
    {
        /// <summary>
        /// Logs once that the development credential validator is active.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "DevelopmentNntpCredentialValidator: all AUTHINFO/SASL attempts return invalid credentials until a real INntpCredentialValidator is registered")]
        public static partial void CredentialValidatorStubActive(ILogger logger);

        /// <summary>
        /// Logs once that reader storage is stubbed.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "DevelopmentNntpArticleStorage: reader data commands are stubbed until INntpArticleStorage is registered")]
        public static partial void ArticleStorageStubActive(ILogger logger);

        /// <summary>
        /// Logs once that transit storage is stubbed.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "DevelopmentNntpTransitStorage: transit commands are stubbed until INntpTransitStorage is registered")]
        public static partial void TransitStorageStubActive(ILogger logger);
    }
}
