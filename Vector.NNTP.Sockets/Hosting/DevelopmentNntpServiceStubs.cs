// <copyright file="DevelopmentNntpServiceStubs.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: development stubs until RADIUS and storage workers are wired.

using Vector.NNTP.Sockets.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        /// are not advertised. <see cref="INntpSessionAdmissionTracker"/> is required by
        /// <see cref="Transport.NntpSessionRunner"/> for session teardown. Production hosts (for example
        /// <c>AddNntpMySqlAuth</c>) replace these stubs via <c>AddSingleton</c>.
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
            services.TryAddSingleton<INntpSessionAdmissionTracker, DevelopmentNntpSessionAdmissionTracker>();
            return services;
        }

        /// <summary>
        /// No-op admission tracker that admits all sessions (no per-account limits until MySQL auth is wired).
        /// </summary>
        private sealed class DevelopmentNntpSessionAdmissionTracker : INntpSessionAdmissionTracker
        {
            /// <inheritdoc />
            public bool TryEnter(NntpSessionPolicy policy, IPAddress clientIp)
            {
                _ = policy;
                _ = clientIp;
                return true;
            }

            /// <inheritdoc />
            public void Leave(NntpSessionPolicy policy, IPAddress clientIp)
            {
                _ = policy;
                _ = clientIp;
            }
        }

        /// <summary>
        /// Empty SCRAM store so DI can construct <see cref="Transport.NntpCommandDispatcher"/> without SCRAM keys in the database.
        /// </summary>
        private sealed class DevelopmentScramCredentialStore : IScramCredentialStore
        {
            /// <inheritdoc />
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
            /// <inheritdoc />
            public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
            {
                _ = username;
                secret = ReadOnlyMemory<byte>.Empty;
                return false;
            }
        }

        private sealed class DevelopmentNntpCredentialValidator(ILogger<DevelopmentNntpCredentialValidator> logger) : INntpCredentialValidator
        {
            private readonly ILogger<DevelopmentNntpCredentialValidator> _logger = logger;
            private int _logged;

            public ValueTask<NntpAuthResult> ValidatePasswordAsync(
                string username,
                string password,
                IPAddress clientIp,
                bool isTls,
                CancellationToken cancellationToken)
            {
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

        private sealed class DevelopmentNntpArticleStorage(ILogger<DevelopmentNntpArticleStorage> logger) : INntpArticleStorage
        {
            private readonly ILogger<DevelopmentNntpArticleStorage> _logger = logger;
            private int _logged;

            public ValueTask<NntpGroupInfo?> SelectGroupAsync(string groupName, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = groupName;
                _ = cancellationToken;
                return ValueTask.FromResult<NntpGroupInfo?>(null);
            }

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

            public ValueTask<NntpPostResult> PostArticleAsync(ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = articleBytes;
                _ = cancellationToken;
                return ValueTask.FromResult(new NntpPostResult(false, null));
            }

            private void LogOnce()
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                {
                    DevelopmentNntpServiceStubsLog.ArticleStorageStubActive(_logger);
                }
            }
        }

        private sealed class DevelopmentNntpTransitStorage(ILogger<DevelopmentNntpTransitStorage> logger) : INntpTransitStorage
        {
            private int _logged;

            public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

            public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

            public ValueTask<bool> TakeThisAsync(string messageId, ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
            {
                LogOnce();
                _ = messageId;
                _ = articleBytes;
                _ = cancellationToken;
                return ValueTask.FromResult(false);
            }

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
