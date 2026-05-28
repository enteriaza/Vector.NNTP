// <copyright file="FakeNntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: test double for AUTHINFO and SASL password mechanisms.

using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// In-memory credential validator for protocol tests.
    /// </summary>
    internal sealed class FakeNntpCredentialValidator : INntpCredentialValidator
    {
        private readonly Dictionary<string, string> _users;
        private readonly Func<string, NntpAccountLimits>? _limitsFactory;
        private readonly Blake3AccountKeyNormalizer _normalizer = new Blake3AccountKeyNormalizer();

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeNntpCredentialValidator"/> class.
        /// </summary>
        /// <param name="users">Username to password map.</param>
        /// <param name="limitsFactory">Optional per-username limits; defaults to unlimited rate account with no admission limits.</param>
        public FakeNntpCredentialValidator(
            IReadOnlyDictionary<string, string> users,
            Func<string, NntpAccountLimits>? limitsFactory = null)
        {
            this._users = new Dictionary<string, string>(users, StringComparer.Ordinal);
            this._limitsFactory = limitsFactory;
        }

        /// <inheritdoc />
        public ValueTask<NntpAuthResult> ValidatePasswordAsync(
            string mechanism,
            string username,
            string password,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken)
        {
            _ = mechanism;
            _ = clientIp;
            _ = isTls;
            _ = cancellationToken;
            if (this._users.TryGetValue(username, out string? expected) && expected == password)
            {
                NntpAccountLimits limits = this._limitsFactory?.Invoke(username)
                    ?? new NntpAccountLimits(username, 'R', 0, 0, 0, 0, string.Empty);
                NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(limits, allowPosting: true, this._normalizer);
                return ValueTask.FromResult(NntpAuthResult.Success(policy));
            }

            return ValueTask.FromResult(NntpAuthResult.InvalidCredentials());
        }
    }
}
