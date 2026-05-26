// <copyright file="SpamAssassinWireTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// SpamAssassinWireTests.cs -- In-process TCP mock of spamd for protocol framing tests.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Vector.NNTP.Filters.SpamAssassin;

namespace Vector.NNTP.Tests.Filters.SpamAssassinTests
{
    /// <summary>
    /// Exercises <see cref="Vector.NNTP.Filters.SpamAssassin.SpamAssassin"/> against a minimal in-process spamd stub.
    /// </summary>
    [TestFixture]
    public sealed class SpamAssassinWireTests
    {
        private TcpListener? listener;
        private CancellationTokenSource? listenCts;
        private int port;

        /// <summary>
        /// Starts a TCP listener that accepts one connection per test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            this.listener = new TcpListener(IPAddress.Loopback, 0);
            this.listener.Start();
            this.port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
            this.listenCts = new CancellationTokenSource();
        }

        /// <summary>
        /// Stops the listener and cancels accept loops.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            this.listenCts?.Cancel();
            this.listener?.Stop();
            this.listenCts?.Dispose();
        }

        /// <summary>
        /// <c>PING</c> receives <c>PONG</c> from the stub.
        /// </summary>
        /// <returns>A task that completes when the assertion finishes.</returns>
        [Test]
        public async Task PingAsync_StubPong_ReturnsTrue()
        {
            Task server = this.RunServerAsync(
                async (client, request) =>
                {
                    Assert.That(request, Does.Contain("PING SPAMC/1.5"));
                    byte[] response = Encoding.ASCII.GetBytes("SPAMD/1.2 0 PONG\r\n");
                    await client.Stream.WriteAsync(response).ConfigureAwait(false);
                    await client.Stream.FlushAsync().ConfigureAwait(false);
                });
            await Task.Delay(50).ConfigureAwait(false);

            Vector.NNTP.Filters.SpamAssassin.SpamAssassin client = this.CreateClient();
            bool alive = await client.PingAsync().ConfigureAwait(false);
            Assert.That(alive, Is.True);
        }

        /// <summary>
        /// <c>CHECK</c> parses the <c>Spam:</c> header from the stub.
        /// </summary>
        /// <returns>A task that completes when the assertion finishes.</returns>
        [Test]
        public async Task CheckAsync_StubHam_ReturnsClassification()
        {
            const string article = "From: poster@example.com\r\nSubject: hi\r\n\r\nbody\r\n";
            Task server = this.RunServerAsync(
                async (client, request) =>
                {
                    Assert.That(request, Does.Contain("CHECK SPAMC/1.5"));
                    Assert.That(request, Does.Contain($"Content-length: {Encoding.UTF8.GetByteCount(article)}"));
                    Assert.That(request, Does.EndWith(article));
                    byte[] response = Encoding.ASCII.GetBytes("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 2.5 / 5.0\r\n\r\n");
                    await client.Stream.WriteAsync(response).ConfigureAwait(false);
                });
            await Task.Delay(50).ConfigureAwait(false);

            Vector.NNTP.Filters.SpamAssassin.SpamAssassin client = this.CreateClient();
            SpamdCheckResult result = await client.CheckAsync(Encoding.UTF8.GetBytes(article)).ConfigureAwait(false);
            Assert.That(result.IsSpam, Is.False);
            Assert.That(result.Score, Is.EqualTo(2.5).Within(0.001));
            Assert.That(result.Threshold, Is.EqualTo(5.0).Within(0.001));
        }

        /// <summary>
        /// <c>PROCESS</c> returns a Content-length body from the stub.
        /// </summary>
        /// <returns>A task that completes when the assertion finishes.</returns>
        [Test]
        public async Task ProcessAsync_StubProcess_ReturnsModifiedArticle()
        {
            const string input = "From: a@b\r\n\r\nx\r\n";
            const string output = "From: a@b\r\nX-Spam-Status: No\r\n\r\nx\r\n";
            Task server = this.RunServerAsync(
                async (client, request) =>
                {
                    Assert.That(request, Does.Contain("PROCESS SPAMC/1.5"));
                    string header = $"SPAMD/1.1 0 EX_OK\r\nContent-length: {Encoding.UTF8.GetByteCount(output)}\r\n\r\n";
                    await client.Stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
                    await client.Stream.WriteAsync(Encoding.UTF8.GetBytes(output)).ConfigureAwait(false);
                });
            await Task.Delay(50).ConfigureAwait(false);

            Vector.NNTP.Filters.SpamAssassin.SpamAssassin client = this.CreateClient();
            SpamdProcessResult result = await client.ProcessAsync(Encoding.UTF8.GetBytes(input)).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(result.ProcessedArticle), Is.EqualTo(output));
        }

        /// <summary>
        /// Non-zero spamd exit code surfaces as <see cref="SpamdProtocolException"/>.
        /// </summary>
        /// <returns>A task that completes when the assertion finishes.</returns>
        [Test]
        public async Task CheckAsync_StubError_ThrowsSpamdProtocolException()
        {
            const string article = "From: a@b\r\n\r\n\r\n";
            Task server = this.RunServerAsync(
                async (client, _) =>
                {
                    byte[] response = Encoding.ASCII.GetBytes("SPAMD/1.1 73 Can't create user directory\r\n");
                    await client.Stream.WriteAsync(response).ConfigureAwait(false);
                });
            await Task.Delay(50).ConfigureAwait(false);

            Vector.NNTP.Filters.SpamAssassin.SpamAssassin client = this.CreateClient();
            try
            {
                await client.CheckAsync(Encoding.UTF8.GetBytes(article)).ConfigureAwait(false);
                Assert.Fail("Expected SpamdProtocolException was not thrown.");
            }
            catch (SpamdProtocolException ex)
            {
                Assert.That(ex.ExitCode, Is.EqualTo(73));
            }
        }

        /// <summary>
        /// Builds a client aimed at the loopback stub port.
        /// </summary>
        /// <returns>Configured <see cref="Vector.NNTP.Filters.SpamAssassin.SpamAssassin"/> instance.</returns>
        private Vector.NNTP.Filters.SpamAssassin.SpamAssassin CreateClient() =>
            new(
                new SpamAssassinOptions
                {
                    Host = "127.0.0.1",
                    Port = this.port,
                    ConnectTimeoutMilliseconds = 5_000,
                    OperationTimeoutMilliseconds = 10_000,
                });

        /// <summary>
        /// Accepts connections until the test tears down.
        /// </summary>
        /// <param name="handler">Stub logic for each accepted client.</param>
        /// <returns>Background accept loop.</returns>
        private Task RunServerAsync(Func<ClientConnection, string, Task> handler)
        {
            return Task.Run(
                async () =>
                {
                    while (!this.listenCts!.IsCancellationRequested)
                    {
                        TcpClient tcp = await this.listener!.AcceptTcpClientAsync(this.listenCts.Token).ConfigureAwait(false);
                        _ = this.HandleClientAsync(tcp, handler);
                    }
                },
                this.listenCts!.Token);
        }

        /// <summary>
        /// Reads the spamc request then runs the stub handler.
        /// </summary>
        /// <param name="tcp">Accepted client socket.</param>
        /// <param name="handler">Stub response logic.</param>
        /// <returns>A task that completes when the handler finishes.</returns>
        private async Task HandleClientAsync(TcpClient tcp, Func<ClientConnection, string, Task> handler)
        {
            using (tcp)
            {
                NetworkStream stream = tcp.GetStream();
                using MemoryStream requestBuffer = new();
                byte[] buf = new byte[4096];
                while (true)
                {
                    int read = await stream.ReadAsync(buf).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    requestBuffer.Write(buf, 0, read);
                }

                string request = Encoding.UTF8.GetString(requestBuffer.ToArray());
                await handler(new ClientConnection(stream), request).ConfigureAwait(false);
            }
        }

        /// <summary>TCP client stream wrapper for the stub handler.</summary>
        private sealed class ClientConnection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ClientConnection"/> class.
            /// </summary>
            /// <param name="stream">Connected stream.</param>
            public ClientConnection(NetworkStream stream) => this.Stream = stream;

            /// <summary>Gets the connected network stream.</summary>
            public NetworkStream Stream { get; }
        }
    }
}
