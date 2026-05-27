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

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeNntpCredentialValidator"/> class.
        /// </summary>
        /// <param name="users">Username to password map.</param>
        public FakeNntpCredentialValidator(IReadOnlyDictionary<string, string> users)
        {
            this._users = new Dictionary<string, string>(users, StringComparer.Ordinal);
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
                return ValueTask.FromResult(NntpAuthResult.Success(new NntpSessionPolicy(username, allowPosting: true, 'R', string.Empty, 0, 0, 0, 0)));
            }

            return ValueTask.FromResult(NntpAuthResult.InvalidCredentials());
        }
    }
}
