// <copyright file="NntpAuthenticationService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: AUTHINFO USER/PASS and SASL mechanism orchestration.

using Vector.NNTP.Sockets.Authentication.Sasl;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Handles AUTHINFO USER/PASS and SASL PLAIN, LOGIN, SCRAM, and CRAM-MD5 on the NNTP wire.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpAuthenticationService"/> class.
    /// </remarks>
    /// <param name="validator">Password validator for USER/PASS, PLAIN, and LOGIN.</param>
    /// <param name="scramStore">Optional SCRAM credential store.</param>
    /// <param name="cramStore">Optional CRAM-MD5 secret store.</param>
    public sealed class NntpAuthenticationService(
        INntpCredentialValidator validator,
        IScramCredentialStore? scramStore = null,
        ICramMd5CredentialStore? cramStore = null)
    {
        private readonly INntpCredentialValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        private readonly IScramCredentialStore? _scramStore = scramStore;
        private readonly ICramMd5CredentialStore? _cramStore = cramStore;

        /// <summary>
        /// Handles an AUTHINFO command line.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        public async ValueTask HandleAuthInfoAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            if (!session.IsAuthInfoPermitted)
            {
                await session.Writer.WriteLineAsync("483 Encryption required", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (session.Connection.IsAuthenticated)
            {
                await session.Writer.WriteLineAsync("502 Already authenticated", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "USER"))
            {
                await HandleUserAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "PASS"))
            {
                await HandlePassAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "SASL"))
            {
                await HandleSaslAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            await session.Writer.WriteLineAsync("501 AUTHINFO command not recognized", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a SASL continuation line (383 response payload).
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="payload">Base64 or plain continuation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when handled.</returns>
        public async ValueTask HandleSaslContinuationAsync(NntpSession session, string payload, CancellationToken cancellationToken)
        {
            if (session.State.AuthenticationState != AuthenticationState.SaslInProgress)
            {
                await session.Writer.WriteLineAsync("503 No SASL exchange in progress", cancellationToken).ConfigureAwait(false);
                return;
            }

            string mech = session.State.PendingSaslMechanism ?? string.Empty;
            if (payload == "*")
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication cancelled", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("CRAM-MD5", StringComparison.OrdinalIgnoreCase) && session.State.SaslServerState is string challenge)
            {
                await HandleCramResponseAsync(session, payload, challenge, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.StartsWith("SCRAM-", StringComparison.OrdinalIgnoreCase) && session.State.SaslServerState is ScramMechanism scram)
            {
                string? serverFinal = scram.TryFinish(DecodeMaybeBase64(payload));
                if (serverFinal is null)
                {
                    ResetAuth(session);
                    await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await session.Writer.WriteLineAsync($"235 {serverFinal}", cancellationToken).ConfigureAwait(false);
                session.Connection.SetAuthenticated(new NntpSessionPolicy("scram-user", allowPosting: true, 'R', string.Empty, 0, 0, 0, 0));
                ResetAuth(session);
                return;
            }

            await session.Writer.WriteLineAsync("503 SASL continuation not supported", cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandleUserAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            string? user = ExtractLastToken(line.AsSpan());
            if (string.IsNullOrEmpty(user))
            {
                await session.Writer.WriteLineAsync("501 USER requires argument", cancellationToken).ConfigureAwait(false);
                return;
            }

            session.State.PendingAuthInfoUser = user;
            session.State.AuthenticationState = AuthenticationState.AuthInfoUserPending;
            await session.Writer.WriteLineAsync("381 Password required", cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandlePassAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            if (session.State.AuthenticationState != AuthenticationState.AuthInfoUserPending)
            {
                await session.Writer.WriteLineAsync("503 AUTHINFO USER required first", cancellationToken).ConfigureAwait(false);
                return;
            }

            string? pass = ExtractLastToken(line.AsSpan());
            string user = session.State.PendingAuthInfoUser ?? string.Empty;
            NntpAuthResult result = await _validator.ValidatePasswordAsync(
                user,
                pass ?? string.Empty,
                session.Connection.ClientRemoteEndPoint.Address,
                session.State.IsTlsActive,
                cancellationToken).ConfigureAwait(false);
            await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
            if (result.Status != NntpAuthStatus.Success)
            {
                session.State.PendingAuthInfoUser = null;
                session.State.AuthenticationState = AuthenticationState.None;
            }
        }

        private async ValueTask HandleSaslAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            string? mech = ExtractMechanism(line.AsSpan());
            if (string.IsNullOrEmpty(mech))
            {
                await session.Writer.WriteLineAsync("501 SASL mechanism required", cancellationToken).ConfigureAwait(false);
                return;
            }

            string? initial = ExtractInitialResponse(line.AsSpan());
            session.State.PendingSaslMechanism = mech;
            session.State.AuthenticationState = AuthenticationState.SaslInProgress;

            if (mech.Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePlainAsync(session, initial, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("LOGIN", StringComparison.OrdinalIgnoreCase))
            {
                await session.Writer.WriteLineAsync("334 VXNlcm5hbWU6", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.StartsWith("SCRAM-", StringComparison.OrdinalIgnoreCase))
            {
                await HandleScramStartAsync(session, mech, initial, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("CRAM-MD5", StringComparison.OrdinalIgnoreCase))
            {
                string challenge = CramMd5Mechanism.CreateChallenge();
                session.State.SaslServerState = challenge;
                await session.Writer.WriteLineAsync($"334 {challenge}", cancellationToken).ConfigureAwait(false);
                return;
            }

            ResetAuth(session);
            await session.Writer.WriteLineAsync("503 Mechanism not supported", cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandlePlainAsync(NntpSession session, string? initial, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(initial))
            {
                await session.Writer.WriteLineAsync("383 Send credentials", cancellationToken).ConfigureAwait(false);
                return;
            }

            string decoded = DecodeMaybeBase64(initial);
            string[] parts = decoded.Split('\0');
            if (parts.Length < 3)
            {
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            NntpAuthResult result = await _validator.ValidatePasswordAsync(
                parts[1],
                parts[2],
                session.Connection.ClientRemoteEndPoint.Address,
                session.State.IsTlsActive,
                cancellationToken).ConfigureAwait(false);
            await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandleScramStartAsync(NntpSession session, string mech, string? initial, CancellationToken cancellationToken)
        {
            if (_scramStore is null || string.IsNullOrEmpty(initial))
            {
                await session.Writer.WriteLineAsync("503 SCRAM not available", cancellationToken).ConfigureAwait(false);
                return;
            }

            string clientFirst = DecodeMaybeBase64(initial);
            if (!ScramMechanismBegin.TryGetUsername(clientFirst, out string? username) ||
                !_scramStore.TryGetScramCredential(username!, out ScramStoredCredential? cred))
            {
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            (ScramMechanism state, string serverFirst) = ScramMechanism.Begin(mech, clientFirst, cred);
            session.State.SaslServerState = state;
            await session.Writer.WriteLineAsync($"383 {serverFirst}", cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandleCramResponseAsync(NntpSession session, string payload, string challenge, CancellationToken cancellationToken)
        {
            if (_cramStore is null)
            {
                await session.Writer.WriteLineAsync("503 CRAM-MD5 not available", cancellationToken).ConfigureAwait(false);
                return;
            }

            string decoded = DecodeMaybeBase64(payload);
            int space = decoded.IndexOf(' ');
            if (space <= 0)
            {
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            string user = decoded[..space];
            if (!_cramStore.TryGetCramSecret(user, out ReadOnlyMemory<byte> secret) ||
                !CramMd5Mechanism.Verify(user, decoded, challenge, secret.Span))
            {
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            session.Connection.SetAuthenticated(new NntpSessionPolicy(user, allowPosting: true, 'R', string.Empty, 0, 0, 0, 0));
            ResetAuth(session);
            await session.Writer.WriteLineAsync("235 Authentication succeeded", cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteAuthResultAsync(NntpSession session, NntpAuthResult result, CancellationToken cancellationToken)
        {
            switch (result.Status)
            {
                case NntpAuthStatus.Success:
                    session.Connection.SetAuthenticated(result.Policy!);
                    ResetAuth(session);
                    await session.Writer.WriteLineAsync("281 Authentication accepted", cancellationToken).ConfigureAwait(false);
                    break;
                case NntpAuthStatus.TransientFailure:
                    await session.Writer.WriteLineAsync("503 Temporary authentication failure", cancellationToken).ConfigureAwait(false);
                    break;
                case NntpAuthStatus.InvalidCredentials:
                    break;
                default:
                    await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private void ResetAuth(NntpSession session)
        {
            session.State.AuthenticationState = AuthenticationState.None;
            session.State.PendingAuthInfoUser = null;
            session.State.PendingSaslMechanism = null;
            session.State.SaslServerState = null;
        }

        private static bool ContainsToken(string line, string token)
        {
            return line.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractLastToken(ReadOnlySpan<char> line)
        {
            int i = line.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            int end = i;
            while (i >= 0 && !char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            return i < end ? line[(i + 1)..(end + 1)].ToString() : null;
        }

        private static string? ExtractMechanism(ReadOnlySpan<char> line)
        {
            int sasl = line.IndexOf("SASL".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (sasl < 0)
            {
                return null;
            }

            ReadOnlySpan<char> rest = line[(sasl + 4)..].TrimStart();
            int space = rest.IndexOf(' ');
            return space < 0 ? rest.ToString() : rest[..space].ToString();
        }

        private static string? ExtractInitialResponse(ReadOnlySpan<char> line)
        {
            int sasl = line.IndexOf("SASL".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (sasl < 0)
            {
                return null;
            }

            ReadOnlySpan<char> rest = line[(sasl + 4)..].TrimStart();
            int space = rest.IndexOf(' ');
            return space < 0 ? null : rest[(space + 1)..].Trim().ToString();
        }

        private static string DecodeMaybeBase64(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return value;
            }
        }
    }

    /// <summary>
    /// Helper to extract SCRAM username from client-first message.
    /// </summary>
    internal static class ScramMechanismBegin
    {
        /// <summary>
        /// Parses the username (n=) from client-first.
        /// </summary>
        /// <param name="clientFirst">Client-first message.</param>
        /// <param name="username">Extracted username.</param>
        /// <returns><see langword="true"/> when found.</returns>
        internal static bool TryGetUsername(string clientFirst, [NotNullWhen(true)] out string? username)
        {
            foreach (string part in clientFirst.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("n=", StringComparison.Ordinal))
                {
                    username = part[2..];
                    return true;
                }
            }

            username = null;
            return false;
        }
    }
}
