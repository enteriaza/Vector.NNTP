// <copyright file="NntpAuthenticationTransientTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Transport;
using Vector.NNTP.Tests.Session;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Sockets.Authentication
{
    /// <summary>
    /// Verifies <see cref="NntpAuthenticationService"/> maps credential-store transient failures to 503.
    /// </summary>
    [TestFixture]
    public sealed class NntpAuthenticationTransientTests
    {
        /// <summary>
        /// Ensures SCRAM start returns 503 when the credential store throws a transient exception.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task HandleAuthInfoAsync_ScramStoreTransient_Returns503()
        {
            string clientFirst = "n,,n=testuser,r=clientnonce";
            string initial = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirst));
            string line = $"AUTHINFO SASL SCRAM-SHA-256 {initial}";

            string response = await RunAuthInfoLineAsync(
                new ThrowingScramCredentialStore(),
                cramStore: null,
                line).ConfigureAwait(false);

            Assert.That(response, Does.StartWith("503 Temporary authentication failure"));
        }

        /// <summary>
        /// Ensures SCRAM start returns 503 when the credential store is slow and then fails transiently.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task HandleAuthInfoAsync_ScramStoreSlow_Returns503WithinBudget()
        {
            string clientFirst = "n,,n=testuser,r=clientnonce";
            string initial = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirst));
            string line = $"AUTHINFO SASL SCRAM-SHA-256 {initial}";
            Stopwatch stopwatch = Stopwatch.StartNew();

            string response = await RunAuthInfoLineAsync(
                new SlowThrowingScramCredentialStore(TimeSpan.FromMilliseconds(200)),
                cramStore: null,
                line).ConfigureAwait(false);

            stopwatch.Stop();
            Assert.That(response, Does.StartWith("503 Temporary authentication failure"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        }

        /// <summary>
        /// Ensures CRAM continuation returns 503 when the credential store throws a transient exception.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task HandleSaslContinuationAsync_CramStoreTransient_Returns503()
        {
            NntpAuthenticationService auth = CreateAuthService(
                scramStore: null,
                cramStore: new ThrowingCramCredentialStore());
            (NntpSession session, PipeReader responseReader) = CreateSession();
            session.State.IsTlsActive = true;

            await auth.HandleAuthInfoAsync(session, "AUTHINFO SASL CRAM-MD5", CancellationToken.None).ConfigureAwait(false);
            string challengeLine = await ReadResponseLineAsync(responseReader).ConfigureAwait(false);
            Assert.That(challengeLine, Does.StartWith("334 "));

            string cramResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser 00000000"));
            await auth.HandleSaslContinuationAsync(session, cramResponse, CancellationToken.None).ConfigureAwait(false);
            string response = await ReadResponseLineAsync(responseReader).ConfigureAwait(false);

            Assert.That(response, Does.StartWith("503 Temporary authentication failure"));
        }

        /// <summary>
        /// Ensures CRAM continuation returns 503 when the credential store is slow and then fails transiently.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task HandleSaslContinuationAsync_CramStoreSlow_Returns503WithinBudget()
        {
            NntpAuthenticationService auth = CreateAuthService(
                scramStore: null,
                cramStore: new SlowThrowingCramCredentialStore(TimeSpan.FromMilliseconds(200)));
            (NntpSession session, PipeReader responseReader) = CreateSession();
            session.State.IsTlsActive = true;
            Stopwatch stopwatch = Stopwatch.StartNew();

            await auth.HandleAuthInfoAsync(session, "AUTHINFO SASL CRAM-MD5", CancellationToken.None).ConfigureAwait(false);
            string challengeLine = await ReadResponseLineAsync(responseReader).ConfigureAwait(false);
            Assert.That(challengeLine, Does.StartWith("334 "));

            string cramResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser 00000000"));
            await auth.HandleSaslContinuationAsync(session, cramResponse, CancellationToken.None).ConfigureAwait(false);
            string response = await ReadResponseLineAsync(responseReader).ConfigureAwait(false);

            stopwatch.Stop();
            Assert.That(response, Does.StartWith("503 Temporary authentication failure"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        }

        /// <summary>
        /// Runs a single AUTHINFO line through the authentication service and returns the response line.
        /// </summary>
        /// <param name="scramStore">Optional SCRAM credential store.</param>
        /// <param name="cramStore">Optional CRAM credential store.</param>
        /// <param name="line">AUTHINFO command line.</param>
        /// <returns>First response line written by the service.</returns>
        private static async Task<string> RunAuthInfoLineAsync(
            IScramCredentialStore? scramStore,
            ICramMd5CredentialStore? cramStore,
            string line)
        {
            NntpAuthenticationService auth = CreateAuthService(scramStore, cramStore);
            (NntpSession session, PipeReader responseReader) = CreateSession();
            session.State.IsTlsActive = true;
            await auth.HandleAuthInfoAsync(session, line, CancellationToken.None).ConfigureAwait(false);
            return await ReadResponseLineAsync(responseReader).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an authentication service with fake validator and optional SASL stores.
        /// </summary>
        /// <param name="scramStore">Optional SCRAM store.</param>
        /// <param name="cramStore">Optional CRAM store.</param>
        /// <returns>Configured authentication service.</returns>
        private static NntpAuthenticationService CreateAuthService(
            IScramCredentialStore? scramStore,
            ICramMd5CredentialStore? cramStore)
        {
            NntpSessionTestServices.NntpSessionTestBundle bundle = NntpSessionTestServices.CreateDefault();
            return new NntpAuthenticationService(
                new FakeNntpCredentialValidator(new Dictionary<string, string>()),
                bundle.Coordinator,
                bundle.Database,
                bundle.BlockQuota,
                bundle.RateAllocation,
                bundle.IdleOptions,
                scramStore,
                cramStore);
        }

        /// <summary>
        /// Creates a pipe-backed reader session for authentication handler tests.
        /// </summary>
        /// <returns>Configured session and the pipe reader for server responses.</returns>
        private static (NntpSession Session, PipeReader ResponseReader) CreateSession()
        {
            Pipe clientToServer = new Pipe();
            Pipe serverToClient = new Pipe();
            var options = Options.Create(new NntpServerOptions
            {
                NodeName = "test-node",
                ServerIdentification = "VectorNNTPD-Test",
                IdleTimeout = TimeSpan.FromSeconds(5),
                RequireTlsForAuthInfo = false,
            });
            IPEndPoint remote = new IPEndPoint(IPAddress.Loopback, 12345);
            NntpConnectionContext context = new NntpConnectionContext(
                Guid.NewGuid().ToString("N"),
                remote,
                remote,
                NntpHostRole.Reader,
                options.Value.NodeName);
            NntpSessionState state = new NntpSessionState();
            NntpPipeTransport transport = new NntpPipeTransport(clientToServer.Reader, serverToClient.Writer);
            NntpSession session = new NntpSession(context, state, new NntpReaderHostProfile(), options, transport, null);
            return (session, serverToClient.Reader);
        }

        /// <summary>
        /// Reads one CRLF-terminated response line from the session output pipe.
        /// </summary>
        /// <param name="reader">Server-to-client pipe reader.</param>
        /// <returns>Response line without CRLF.</returns>
        private static async Task<string> ReadResponseLineAsync(PipeReader reader)
        {
            StringBuilder buffer = new StringBuilder();
            while (true)
            {
                string pending = buffer.ToString();
                int lineEnd = pending.IndexOf("\r\n", StringComparison.Ordinal);
                if (lineEnd >= 0)
                {
                    return pending[..lineEnd];
                }

                ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                foreach (ReadOnlyMemory<byte> segment in result.Buffer)
                {
                    buffer.Append(Encoding.ASCII.GetString(segment.Span));
                }

                reader.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted && buffer.Length == 0)
                {
                    throw new InvalidOperationException("Expected a complete response line.");
                }
            }
        }

        /// <summary>
        /// SCRAM store that always throws transient failures.
        /// </summary>
        private sealed class ThrowingScramCredentialStore : IScramCredentialStore
        {
            /// <inheritdoc />
            public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
            {
                _ = username;
                credential = null;
                throw new NntpCredentialStoreTransientException("Simulated backend outage.");
            }
        }

        /// <summary>
        /// SCRAM store that delays before throwing a transient failure.
        /// </summary>
        private sealed class SlowThrowingScramCredentialStore : IScramCredentialStore
        {
            /// <summary>
            /// Simulated backend delay.
            /// </summary>
            private readonly TimeSpan _delay;

            /// <summary>
            /// Initializes a new instance of the <see cref="SlowThrowingScramCredentialStore"/> class.
            /// </summary>
            /// <param name="delay">Delay before throwing.</param>
            public SlowThrowingScramCredentialStore(TimeSpan delay)
            {
                this._delay = delay;
            }

            /// <inheritdoc />
            public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
            {
                _ = username;
                Thread.Sleep(this._delay);
                credential = null;
                throw new NntpCredentialStoreTransientException("Simulated slow backend outage.");
            }
        }

        /// <summary>
        /// CRAM store that delays before throwing a transient failure.
        /// </summary>
        private sealed class SlowThrowingCramCredentialStore : ICramMd5CredentialStore
        {
            /// <summary>
            /// Simulated backend delay.
            /// </summary>
            private readonly TimeSpan _delay;

            /// <summary>
            /// Initializes a new instance of the <see cref="SlowThrowingCramCredentialStore"/> class.
            /// </summary>
            /// <param name="delay">Delay before throwing.</param>
            public SlowThrowingCramCredentialStore(TimeSpan delay)
            {
                this._delay = delay;
            }

            /// <inheritdoc />
            public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
            {
                _ = username;
                Thread.Sleep(this._delay);
                secret = ReadOnlyMemory<byte>.Empty;
                throw new NntpCredentialStoreTransientException("Simulated slow backend outage.");
            }
        }

        /// <summary>
        /// CRAM store that always throws transient failures.
        /// </summary>
        private sealed class ThrowingCramCredentialStore : ICramMd5CredentialStore
        {
            /// <inheritdoc />
            public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
            {
                _ = username;
                secret = ReadOnlyMemory<byte>.Empty;
                throw new NntpCredentialStoreTransientException("Simulated backend outage.");
            }
        }
    }
}
