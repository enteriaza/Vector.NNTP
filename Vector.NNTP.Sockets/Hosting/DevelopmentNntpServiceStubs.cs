// <copyright file="DevelopmentNntpServiceStubs.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: development stubs until RADIUS and storage workers are wired.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Registers development-only NNTP service stubs for hosts without production auth/storage.
    /// </summary>
    public static class DevelopmentNntpServiceStubs
    {
        /// <summary>
        /// Registers <see cref="INntpCredentialValidator"/>, <see cref="INntpArticleStorage"/>,
        /// <see cref="INntpTransitStorage"/>, and optional SASL credential stores for hosts without production auth.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="IScramCredentialStore"/> and <see cref="ICramMd5CredentialStore"/> are required by
        /// <see cref="Transport.NntpCommandDispatcher"/> and <see cref="NntpAuthenticationService"/> even when SCRAM/CRAM
        /// are not advertised. Session admission is handled by <see cref="INntpSessionCoordinator"/> from
        /// <c>Vector.NNTP.Session</c>. Production hosts replace credential validation via <c>AddNntpMySqlAuth</c>.
        /// </para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpSocketsDevelopmentStubs(this IServiceCollection services)
        {
            services.TryAddSingleton<INntpCredentialValidator, DevelopmentNntpCredentialValidator>();
            services.TryAddSingleton<INntpArticleStorage, DevelopmentNntpArticleStorage>();
            services.TryAddSingleton<INntpTransitStorage, DevelopmentNntpTransitStorage>();
            services.TryAddSingleton<IScramCredentialStore, DevelopmentScramCredentialStore>();
            services.TryAddSingleton<ICramMd5CredentialStore, DevelopmentCramMd5CredentialStore>();
            return services;
        }

        /// <summary>
        /// Empty SCRAM store so DI can construct <see cref="Transport.NntpCommandDispatcher"/> without SCRAM keys in the database.
        /// </summary>
        private sealed class DevelopmentScramCredentialStore : IScramCredentialStore
        {
            /// <summary>
            /// Always returns <see langword="false"/> so development hosts start without SCRAM credential material.
            /// </summary>
            /// <param name="username">NNTP username (ignored).</param>
            /// <param name="credential">Always <see langword="null"/>.</param>
            /// <returns><see langword="false"/>.</returns>
            public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
            {
                _ = username;
                credential = null;
                return false;
            }
        }

        /// <summary>
        /// Empty CRAM-MD5 store until a host registers <see cref="ICramMd5CredentialStore"/> (for example MySQL <c>nntpusers</c>).
        /// </summary>
        private sealed class DevelopmentCramMd5CredentialStore : ICramMd5CredentialStore
        {
            /// <summary>
            /// Always returns <see langword="false"/> so development hosts start without CRAM-MD5 secrets.
            /// </summary>
            /// <param name="username">NNTP username (ignored).</param>
            /// <param name="secret">Always empty.</param>
            /// <returns><see langword="false"/>.</returns>
            public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
            {
                _ = username;
                secret = ReadOnlyMemory<byte>.Empty;
                return false;
            }
        }

        /// <summary>
        /// Development credential validator that always rejects with invalid credentials after a one-time stub notice.
        /// </summary>
        private sealed class DevelopmentNntpCredentialValidator(ILogger<DevelopmentNntpCredentialValidator> logger) : INntpCredentialValidator
        {
            /// <summary>
            /// Logger for the one-time stub activation message.
            /// </summary>
            private readonly ILogger<DevelopmentNntpCredentialValidator> _logger = logger;

            /// <summary>
            /// Non-zero after the stub activation log has been emitted.
            /// </summary>
            private int _logged;

            /// <summary>
            /// Rejects all password validation attempts with <see cref="NntpAuthResult.InvalidCredentials"/>.
            /// </summary>
            /// <param name="mechanism">Auth mechanism name (ignored).</param>
            /// <param name="username">NNTP username (ignored).</param>
            /// <param name="password">Presented password (ignored).</param>
            /// <param name="clientIp">Client IP (ignored).</param>
            /// <param name="isTls">Whether the connection is TLS-protected (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with invalid credentials.</returns>
            public ValueTask<NntpAuthResult> ValidatePasswordAsync(
                string mechanism,
                string username,
                string password,
                IPAddress clientIp,
                bool isTls,
                CancellationToken cancellationToken)
            {
                _ = mechanism;
                _ = username;
                _ = password;
                _ = clientIp;
                _ = isTls;
                _ = cancellationToken;
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                {
                    DevelopmentNntpServiceStubsLog.CredentialValidatorStubActive(_logger);
                }

                return ValueTask.FromResult(NntpAuthResult.InvalidCredentials());
            }
        }

        /// <summary>
        /// Development article storage stub that returns empty results after a one-time activation log.
        /// </summary>
        private sealed class DevelopmentNntpArticleStorage(ILogger<DevelopmentNntpArticleStorage> logger) : INntpArticleStorage
        {
            /// <summary>
            /// Logger for the one-time stub activation message.
            /// </summary>
            private readonly ILogger<DevelopmentNntpArticleStorage> _logger = logger;

            /// <summary>
            /// Non-zero after the stub activation log has been emitted.
            /// </summary>
            private int _logged;

            /// <summary>
            /// Returns <see langword="null"/> because no groups are available in the development stub.
            /// </summary>
            /// <param name="groupName">Requested group name (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <see langword="null"/>.</returns>
            public ValueTask<NntpGroupInfo?> SelectGroupAsync(string groupName, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = groupName;
                _ = cancellationToken;
                return ValueTask.FromResult<NntpGroupInfo?>(null);
            }

            /// <summary>
            /// Returns <see langword="null"/> because articles are not stored in the development stub.
            /// </summary>
            /// <param name="groupName">Optional group name (ignored).</param>
            /// <param name="articleNumber">Optional article number (ignored).</param>
            /// <param name="messageId">Optional message identifier (ignored).</param>
            /// <param name="part">Requested article part (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <see langword="null"/>.</returns>
            public ValueTask<NntpArticlePayload?> GetArticleAsync(
                string? groupName,
                long? articleNumber,
                string? messageId,
                NntpArticlePart part,
                CancellationToken cancellationToken)
            {
                LogOnce();
                _ = groupName;
                _ = articleNumber;
                _ = messageId;
                _ = part;
                _ = cancellationToken;
                return ValueTask.FromResult<NntpArticlePayload?>(null);
            }

            /// <summary>
            /// Returns a failed post result because posting is not supported in the development stub.
            /// </summary>
            /// <param name="articleBytes">Article bytes (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <c>Accepted = false</c>.</returns>
            public ValueTask<NntpPostResult> PostArticleAsync(ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = articleBytes;
                _ = cancellationToken;
                return ValueTask.FromResult(new NntpPostResult(false, null));
            }

            /// <summary>
            /// Emits the article storage stub activation log at most once per process.
            /// </summary>
            private void LogOnce()
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                {
                    DevelopmentNntpServiceStubsLog.ArticleStorageStubActive(_logger);
                }
            }
        }

        /// <summary>
        /// Development transit storage stub that declines all CHECK/IHAVE/TAKETHIS operations.
        /// </summary>
        private sealed class DevelopmentNntpTransitStorage(ILogger<DevelopmentNntpTransitStorage> logger) : INntpTransitStorage
        {
            /// <summary>
            /// Non-zero after the stub activation log has been emitted.
            /// </summary>
            private int _logged;

            /// <summary>
            /// Returns <see langword="false"/> because message IDs are not indexed in the development stub.
            /// </summary>
            /// <param name="messageId">Message identifier (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <see langword="false"/>.</returns>
            public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

            /// <summary>
            /// Returns <see langword="false"/> because IHAVE is not supported in the development stub.
            /// </summary>
            /// <param name="messageId">Message identifier (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <see langword="false"/>.</returns>
            public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

            /// <summary>
            /// Returns <see langword="false"/> because TAKETHIS is not supported in the development stub.
            /// </summary>
            /// <param name="messageId">Message identifier (ignored).</param>
            /// <param name="articleBytes">Article bytes (ignored).</param>
            /// <param name="cancellationToken">Cancellation token (ignored).</param>
            /// <returns>A completed task with <see langword="false"/>.</returns>
            public ValueTask<bool> TakeThisAsync(string messageId, ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = articleBytes;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

            /// <summary>
            /// Emits the transit storage stub activation log at most once per process.
            /// </summary>
            private void LogOnce()
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                {
                    DevelopmentNntpServiceStubsLog.TransitStorageStubActive(logger);
                }
            }
        }
    }
}
