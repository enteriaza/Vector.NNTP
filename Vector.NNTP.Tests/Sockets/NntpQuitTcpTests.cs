// <copyright file="NntpQuitTcpTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: loopback TCP QUIT graceful close verification.

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Transport;
using Vector.NNTP.Tests.Session;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Verifies QUIT closes a real TCP socket transport after 205.
    /// </summary>
    [TestFixture]
    public sealed class NntpQuitTcpTests
    {
        /// <summary>
        /// Verifies QUIT on a loopback TCP session returns 205 and EOF.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Quit_OnTcpTransport_Returns205AndEof()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task serverTask = this.AcceptAndRunSessionAsync(listener, acceptCts.Token);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            string? greeting = await reader.ReadLineAsync().ConfigureAwait(false);
            Assert.That(greeting, Does.StartWith("201 "));

            await writer.WriteLineAsync("QUIT").ConfigureAwait(false);
            string? quitLine = await reader.ReadLineAsync().ConfigureAwait(false);
            Assert.That(quitLine, Does.StartWith("205 "));

            client.Client.Shutdown(SocketShutdown.Send);
            byte[] buffer = new byte[16];
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(0));

            acceptCts.Cancel();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when accept loop is canceled
            }
            catch (TimeoutException)
            {
                Assert.Fail("Server session did not complete within 5 seconds after QUIT.");
            }
        }

        /// <summary>
        /// Accepts one TCP connection and runs a reader session until disconnect.
        /// </summary>
        /// <param name="listener">Bound loopback listener.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the session ends.</returns>
        private async Task AcceptAndRunSessionAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            Socket socket = await listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            using var sessionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var options = Options.Create(new NntpServerOptions
            {
                NodeName = "test-node",
                ServerIdentification = "VectorNNTPD-TcpTest",
                IdleTimeoutSeconds = 5,
            });
            NntpSessionTestServices.NntpSessionTestBundle session = NntpSessionTestServices.CreateDefault();
            var auth = new NntpAuthenticationService(
                new FakeNntpCredentialValidator(new Dictionary<string, string> { ["alice"] = "secret" }),
                session.Coordinator,
                session.Database,
                session.BlockQuota,
                session.RateAllocation,
                session.IdleOptions);
            var dispatcher = new NntpCommandDispatcher(
                auth,
                new FakeNntpArticleStorage(),
                transitStorage: null,
                historyDatabase: null,
                tlsCertificateSource: null,
                scramCredentialStore: null,
                options,
                new FakeNntpCpuLoadMonitor(),
                NullLogger<NntpCommandDispatcher>.Instance);
            var runner = new NntpSessionRunner(
                dispatcher,
                new NntpReaderHostProfile(),
                options,
                session.Database,
                session.Coordinator,
                session.TransitPeerCoordinator,
                session.QuotaEnforcer,
                tlsCertificateSource: null,
                NullLogger<NntpSessionRunner>.Instance);
            var remote = (IPEndPoint)socket.RemoteEndPoint!;
            var context = new NntpConnectionContext(
                Guid.NewGuid().ToString("N"),
                remote,
                remote,
                NntpHostRole.Reader,
                options.Value.NodeName);
            var transport = new NntpSocketTransport(socket);
            await runner.RunAsync(transport, context, tlsAlreadyActive: false, sessionCts.Token).ConfigureAwait(false);
        }
    }
}
