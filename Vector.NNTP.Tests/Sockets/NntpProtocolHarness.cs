// <copyright file="NntpProtocolHarness.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory duplex pipe harness for golden transcript tests.

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;
using Vector.NNTP.Sockets.Transport;
using Vector.NNTP.Tests.HistoryDB.Fakes;
using Vector.NNTP.Tests.Session;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Runs an NNTP session over in-memory pipes for protocol testing.
    /// </summary>
    internal sealed class NntpProtocolHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToServer;
        private readonly Pipe _serverToClient;
        private readonly Task _serverTask;
        private readonly CancellationTokenSource _cts;
        private readonly StringBuilder _readBuffer = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpProtocolHarness"/> class.
        /// </summary>
        /// <param name="clientToServer">Client-to-server pipe.</param>
        /// <param name="serverToClient">Server-to-client pipe.</param>
        /// <param name="serverTask">Background session task.</param>
        /// <param name="cts">Linked cancellation source.</param>
        private NntpProtocolHarness(Pipe clientToServer, Pipe serverToClient, Task serverTask, CancellationTokenSource cts)
        {
            this._clientToServer = clientToServer;
            this._serverToClient = serverToClient;
            this._serverTask = serverTask;
            this._cts = cts;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await this._cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await this._serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancel
            }

            await this._clientToServer.Reader.CompleteAsync().ConfigureAwait(false);
            await this._clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
            await this._serverToClient.Reader.CompleteAsync().ConfigureAwait(false);
            await this._serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
            this._cts.Dispose();
        }

        /// <summary>
        /// Creates a reader-profile harness with fake storage and credentials.
        /// </summary>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateReader() =>
            Create(new NntpReaderHostProfile(), new FakeNntpArticleStorage(), null, null, scramCredentialStore: null);

        /// <summary>
        /// Creates a reader harness with shared session services and a custom credential validator.
        /// </summary>
        /// <param name="session">Shared session bundle (coordinator must be shared across connections for admission tests).</param>
        /// <param name="validator">Credential validator.</param>
        /// <param name="clientIp">Simulated client IP for admission source-IP limits.</param>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateReader(
            NntpSessionTestServices.NntpSessionTestBundle session,
            FakeNntpCredentialValidator validator,
            IPAddress? clientIp = null) =>
            Create(
                new NntpReaderHostProfile(),
                new FakeNntpArticleStorage(),
                transit: null,
                historyDatabase: null,
                scramCredentialStore: null,
                session,
                validator,
                clientIp);

        /// <summary>
        /// Creates a transit-profile harness with fake transit storage.
        /// </summary>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateTransit() =>
            CreateTransit(new FakeHistoryDatabase());

        /// <summary>
        /// Creates a transit harness with a supplied fake history database.
        /// </summary>
        /// <param name="historyDatabase">History implementation for CHECK and record paths.</param>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateTransit(FakeHistoryDatabase historyDatabase) =>
            Create(
                new NntpTransitHostProfile(),
                null,
                new FakeNntpTransitStorage(),
                historyDatabase,
                scramCredentialStore: null);

        /// <summary>
        /// Creates a reader-profile harness with a supplied SCRAM credential store.
        /// </summary>
        /// <param name="scramCredentialStore">SCRAM credential store used for SASL SCRAM mechanism support.</param>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateReaderWithScram(IScramCredentialStore scramCredentialStore) =>
            Create(new NntpReaderHostProfile(), new FakeNntpArticleStorage(), null, null, scramCredentialStore);

        /// <summary>
        /// Creates a transit-profile harness with a supplied SCRAM credential store.
        /// </summary>
        /// <param name="scramCredentialStore">SCRAM credential store used for SASL SCRAM mechanism support.</param>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateTransitWithScram(IScramCredentialStore scramCredentialStore) =>
            Create(
                new NntpTransitHostProfile(),
                null,
                new FakeNntpTransitStorage(),
                new FakeHistoryDatabase(),
                scramCredentialStore);

        /// <summary>
        /// Creates a transit harness simulating an admitted trusted transit peer (streaming without AUTH).
        /// </summary>
        /// <param name="transitPeerName">Configured transit peer name.</param>
        /// <returns>Connected harness instance.</returns>
        internal static NntpProtocolHarness CreateTransitTrustedPeer(string transitPeerName) =>
            Create(
                new NntpTransitHostProfile(),
                null,
                new FakeNntpTransitStorage(),
                new FakeHistoryDatabase(),
                scramCredentialStore: null,
                transitPeerName: transitPeerName);

        /// <summary>
        /// Authenticates on a transit harness (same fake credentials as reader).
        /// </summary>
        /// <param name="harness">Connected harness.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when authentication succeeds.</returns>
        internal static async Task AuthenticateTransitAsync(
            NntpProtocolHarness harness,
            CancellationToken cancellationToken = default)
        {
            _ = await harness.ReadGreetingAsync(cancellationToken).ConfigureAwait(false);
            await harness.SendAsync("AUTHINFO USER alice", cancellationToken).ConfigureAwait(false);
            _ = await harness.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            await harness.SendAsync("AUTHINFO PASS secret", cancellationToken).ConfigureAwait(false);
            string ok = await harness.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(ok, Does.StartWith("281 "));
        }

        /// <summary>
        /// Reads the initial greeting line.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Greeting line text.</returns>
        internal Task<string> ReadGreetingAsync(CancellationToken cancellationToken = default) =>
            this.ReadLineAsync(cancellationToken);

        /// <summary>
        /// Authenticates with AUTHINFO USER/PASS using the fake alice/secret credentials.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when authentication succeeds.</returns>
        internal async Task AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            _ = await this.ReadGreetingAsync(cancellationToken).ConfigureAwait(false);
            await this.SendAsync("AUTHINFO USER alice", cancellationToken).ConfigureAwait(false);
            _ = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            await this.SendAsync("AUTHINFO PASS secret", cancellationToken).ConfigureAwait(false);
            string ok = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(ok, Does.StartWith("281 "));
        }

        /// <summary>
        /// Sends a dot-stuffed article body terminated by a lone period line.
        /// </summary>
        /// <param name="body">Raw article bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the body is flushed.</returns>
        internal async Task SendArticleBodyAsync(string body, CancellationToken cancellationToken = default)
        {
            foreach (string line in body.Replace("\n", "\r\n", StringComparison.Ordinal).Split("\r\n", StringSplitOptions.None))
            {
                if (line.StartsWith('.'))
                {
                    await this.SendAsync("." + line, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await this.SendAsync(line, cancellationToken).ConfigureAwait(false);
                }
            }

            await this.SendAsync(".", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a TAKETHIS command line followed immediately by a dot-stuffed article body (RFC 4644 pipelining).
        /// </summary>
        /// <param name="messageId">Message-ID argument (including angle brackets).</param>
        /// <param name="body">Raw article bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the command and body are flushed.</returns>
        internal async Task SendTakethisWithArticleAsync(
            string messageId,
            string body,
            CancellationToken cancellationToken = default)
        {
            await this.SendAsync("TAKETHIS " + messageId, cancellationToken).ConfigureAwait(false);
            await this.SendArticleBodyAsync(body, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a command line (CRLF appended).
        /// </summary>
        /// <param name="command">Command without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when bytes are flushed.</returns>
        internal async Task SendAsync(string command, CancellationToken cancellationToken = default)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command + "\r\n");
            await this._clientToServer.Writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await this._clientToServer.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a dot-terminated multi-line response (including the header line, excluding the final dot).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All lines before the terminating dot.</returns>
        internal async Task<List<string>> ReadMultiLineAsync(CancellationToken cancellationToken = default)
        {
            var lines = new List<string>();
            while (true)
            {
                string line = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == ".")
                {
                    return lines;
                }

                lines.Add(line);
            }
        }

        /// <summary>
        /// Reads one CRLF-terminated line from the server.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Line without CRLF.</returns>
        internal async Task<string> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                string buffered = this._readBuffer.ToString();
                int idx = buffered.IndexOf("\r\n", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    string line = buffered[..idx];
                    this._readBuffer.Clear();
                    this._readBuffer.Append(buffered[(idx + 2)..]);
                    return line;
                }

                ReadResult read = await this._serverToClient.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                foreach (ReadOnlyMemory<byte> seg in read.Buffer)
                {
                    this._readBuffer.Append(Encoding.ASCII.GetString(seg.Span));
                }

                this._serverToClient.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted && this._readBuffer.Length == 0)
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Wires pipes and starts the server session task.
        /// </summary>
        /// <param name="profile">Host profile.</param>
        /// <param name="articles">Optional reader storage.</param>
        /// <param name="transit">Optional transit storage.</param>
        /// <param name="historyDatabase">Optional history database for CHECK.</param>
        /// <param name="scramCredentialStore">Optional SCRAM credential store used for CAPABILITIES advertisement.</param>
        /// <param name="session">Optional shared session bundle; defaults to a fresh in-memory stack.</param>
        /// <param name="validator">Optional credential validator; defaults to alice/secret.</param>
        /// <param name="clientIp">Optional simulated client IP for admission tests.</param>
        /// <param name="transitPeerName">Optional trusted transit peer name (skips AUTH for streaming).</param>
        /// <returns>Connected harness.</returns>
        private static NntpProtocolHarness Create(
            INntpHostProfile profile,
            INntpArticleStorage? articles,
            INntpTransitStorage? transit,
            IHistoryDatabase? historyDatabase,
            IScramCredentialStore? scramCredentialStore,
            NntpSessionTestServices.NntpSessionTestBundle? session = null,
            FakeNntpCredentialValidator? validator = null,
            IPAddress? clientIp = null,
            string? transitPeerName = null)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var cts = new CancellationTokenSource();
            var options = Options.Create(new NntpServerOptions
            {
                NodeName = "test-node",
                ServerIdentification = "VectorNNTPD-Test",
                IdleTimeout = TimeSpan.FromSeconds(5),
            });
            NntpSessionTestServices.NntpSessionTestBundle sessionBundle = session ?? NntpSessionTestServices.CreateDefault();
            FakeNntpCredentialValidator credentialValidator = validator
                ?? new FakeNntpCredentialValidator(new Dictionary<string, string> { ["alice"] = "secret" });
            var auth = new NntpAuthenticationService(
                credentialValidator,
                sessionBundle.Coordinator,
                sessionBundle.Database,
                sessionBundle.BlockQuota,
                sessionBundle.RateAllocation,
                sessionBundle.IdleOptions);
            var dispatcher = new NntpCommandDispatcher(
                auth,
                articles,
                transit,
                historyDatabase,
                tlsCertificateSource: null,
                scramCredentialStore: scramCredentialStore,
                NullLogger<NntpCommandDispatcher>.Instance);
            var runner = new NntpSessionRunner(
                dispatcher,
                profile,
                options,
                sessionBundle.Database,
                sessionBundle.Coordinator,
                sessionBundle.TransitPeerCoordinator,
                sessionBundle.QuotaEnforcer,
                tlsCertificateSource: null,
                NullLogger<NntpSessionRunner>.Instance);
            IPAddress ip = clientIp ?? IPAddress.Loopback;
            var remote = new IPEndPoint(ip, 12345);
            var context = new NntpConnectionContext(
                Guid.NewGuid().ToString("N"),
                remote,
                remote,
                profile.Role,
                options.Value.NodeName,
                transitPeerName);
            var transport = new NntpPipeTransport(clientToServer.Reader, serverToClient.Writer);
            Task serverTask = runner.RunAsync(transport, context, tlsAlreadyActive: false, cts.Token);
            return new NntpProtocolHarness(clientToServer, serverToClient, serverTask, cts);
        }
    }
}
