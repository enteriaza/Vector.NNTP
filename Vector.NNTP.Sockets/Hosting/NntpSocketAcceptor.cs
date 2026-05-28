// <copyright file="NntpSocketAcceptor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: TCP accept loop with connection limits and implicit TLS.

using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Proxy;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Accepts TCP connections and spawns per-connection session runners (cleartext and implicit TLS).
    /// </summary>
    internal sealed partial class NntpSocketAcceptor : IDisposable
    {
        private readonly NntpSessionRunner _runner;
        private readonly INntpHostProfile _profile;
        private readonly IOptions<NntpServerOptions> _options;
        private readonly ITlsCertificateSource _tlsCertificateSource;
        private readonly CertificateRenewalService? _renewalService;
        private readonly NntpInFlightSessionTracker _inFlight;
        private readonly ILogger<NntpSocketAcceptor> _logger;
        private int _activeConnections;
        private readonly ConcurrentDictionary<string, int> _connectionsPerClientIp = new(StringComparer.Ordinal);
        private X509Certificate2? _handshakeCertificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketAcceptor"/> class.
        /// </summary>
        /// <param name="runner">Session runner.</param>
        /// <param name="profile">Host profile.</param>
        /// <param name="options">Server options.</param>
        /// <param name="tlsCertificateSource">TLS certificate source.</param>
        /// <param name="renewalService">Optional renewal service for certificate hot reload.</param>
        /// <param name="inFlight">In-flight session tracker.</param>
        /// <param name="logger">Logger.</param>
        public NntpSocketAcceptor(
            NntpSessionRunner runner,
            INntpHostProfile profile,
            IOptions<NntpServerOptions> options,
            ITlsCertificateSource tlsCertificateSource,
            CertificateRenewalService? renewalService,
            NntpInFlightSessionTracker inFlight,
            ILogger<NntpSocketAcceptor> logger)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _tlsCertificateSource = tlsCertificateSource ?? throw new ArgumentNullException(nameof(tlsCertificateSource));
            _renewalService = renewalService;
            _inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handshakeCertificate = _renewalService?.GetCurrentCertificate();
            if (_renewalService is not null)
            {
                _renewalService.CertificateChanged += OnCertificateChanged;
            }
        }

        /// <summary>
        /// Runs cleartext and TLS accept loops until <paramref name="cancellationToken"/> is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when listening stops.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            NntpServerOptions opts = _options.Value;
            List<Task> tasks = [];
            if (opts.Port > 0)
            {
                tasks.Add(RunListenerAsync(opts.BindAddress, opts.Port, implicitTls: false, cancellationToken));
            }

            if (opts.TlsPort > 0)
            {
                tasks.Add(RunListenerAsync(opts.BindAddress, opts.TlsPort, implicitTls: true, cancellationToken));
            }

            if (tasks.Count == 0)
            {
                throw new InvalidOperationException("NntpServer: at least one of Port or TlsPort must be configured.");
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task RunListenerAsync(string bindAddress, int port, bool implicitTls, CancellationToken cancellationToken)
        {
            TcpListener listener = new(IPAddress.Parse(NormalizeBind(bindAddress)), port);
            listener.Start();
            LogListening(bindAddress, port, implicitTls ? "TLS" : "cleartext");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Socket socket = await listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
                    _ = HandleConnectionAsync(socket, implicitTls, cancellationToken);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private bool TryAcquireConnectionSlot(IPEndPoint clientEndPoint)
        {
            NntpServerOptions opts = _options.Value;
            int max = opts.MaxConnections;
            if (max > 0 && Interlocked.Increment(ref _activeConnections) > max)
            {
                _ = Interlocked.Decrement(ref _activeConnections);
                return false;
            }

            int maxPerIp = opts.MaxConnectionsPerClientIp;
            if (maxPerIp > 0)
            {
                string ipKey = clientEndPoint.Address.ToString();
                if (!TryIncrementPerIp(ipKey, maxPerIp))
                {
                    _ = Interlocked.Decrement(ref _activeConnections);
                    return false;
                }
            }

            return true;
        }

        private void ReleaseConnectionSlot(IPEndPoint clientEndPoint)
        {
            _ = Interlocked.Decrement(ref _activeConnections);
            if (_options.Value.MaxConnectionsPerClientIp > 0)
            {
                DecrementPerIp(clientEndPoint.Address.ToString());
            }
        }

        private bool TryIncrementPerIp(string ipKey, int limit)
        {
            while (true)
            {
                int existing = _connectionsPerClientIp.TryGetValue(ipKey, out int current) ? current : 0;
                if (existing >= limit)
                {
                    return false;
                }

                int next = existing + 1;
                if (_connectionsPerClientIp.TryUpdate(ipKey, next, existing))
                {
                    return true;
                }

                if (existing == 0 && _connectionsPerClientIp.TryAdd(ipKey, next))
                {
                    return true;
                }
            }
        }

        private void DecrementPerIp(string ipKey)
        {
            while (_connectionsPerClientIp.TryGetValue(ipKey, out int current))
            {
                int next = current - 1;
                if (next <= 0)
                {
                    if (_connectionsPerClientIp.TryRemove(ipKey, out _))
                    {
                        return;
                    }
                }
                else if (_connectionsPerClientIp.TryUpdate(ipKey, next, current))
                {
                    return;
                }
            }
        }

        private async Task HandleConnectionAsync(Socket socket, bool implicitTls, CancellationToken cancellationToken)
        {
            _inFlight.Enter();
            IPEndPoint clientEndPoint = (IPEndPoint)socket.RemoteEndPoint!;
            bool slotAcquired = false;
            try
            {
                IPEndPoint tcpPeer = clientEndPoint;
                string sessionId = Guid.NewGuid().ToString("N");
                string? proxyLine = null;

                if (_options.Value.EnableProxyProtocol)
                {
                    proxyLine = await ReadProxyLineAsync(socket, cancellationToken).ConfigureAwait(false);
                    (clientEndPoint, _) = ProxyPreambleResolver.Resolve(tcpPeer, proxyLine);
                }

                if (!TryAcquireConnectionSlot(clientEndPoint))
                {
                    socket.Dispose();
                    return;
                }

                slotAcquired = true;

                if (implicitTls)
                {
                    X509Certificate2? cert = _handshakeCertificate
                        ?? await _tlsCertificateSource.GetServerCertificateAsync(cancellationToken).ConfigureAwait(false);
                    if (cert is null)
                    {
                        LogRejectTlsNoCertificate();
                        socket.Dispose();
                        return;
                    }

                    NntpConnectionContext context = new(sessionId, clientEndPoint, tcpPeer, _profile.Role);
                    NetworkStream networkStream = new(socket, ownsSocket: false);
                    SslStream ssl = new(networkStream, leaveInnerStreamOpen: false);
                    await NntpTlsHandshake.AuthenticateServerAsync(ssl, cert, cancellationToken).ConfigureAwait(false);
                    NntpSocketTransport transport = new(socket, ssl);
                    await _runner.RunAsync(transport, context, tlsAlreadyActive: true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    NntpConnectionContext context = new(sessionId, clientEndPoint, tcpPeer, _profile.Role);
                    NntpSocketTransport transport = new(socket);
                    await _runner.RunAsync(transport, context, tlsAlreadyActive: false, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogConnectionClosedWithError(ex);
            }
            finally
            {
                if (slotAcquired)
                {
                    ReleaseConnectionSlot(clientEndPoint);
                }

                _inFlight.Leave();
            }
        }

        private static async Task<string?> ReadProxyLineAsync(Socket socket, CancellationToken cancellationToken)
        {
            using NetworkStream stream = new(socket, ownsSocket: false);
            byte[] buffer = new byte[512];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                int crlf = Array.IndexOf(buffer, (byte)'\n', 0, total);
                if (crlf >= 0)
                {
                    int end = crlf;
                    if (end > 0 && buffer[end - 1] == (byte)'\r')
                    {
                        end--;
                    }

                    return Encoding.ASCII.GetString(buffer, 0, end);
                }
            }

            return total > 0 ? Encoding.ASCII.GetString(buffer, 0, total) : null;
        }

        private void OnCertificateChanged(X509Certificate2 certificate)
        {
            _ = Interlocked.Exchange(ref _handshakeCertificate, certificate);
            LogTlsCertificateUpdated(certificate.Thumbprint);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_renewalService is not null)
            {
                _renewalService.CertificateChanged -= OnCertificateChanged;
            }
        }

        private static string NormalizeBind(string address)
        {
            return string.IsNullOrWhiteSpace(address) || address == "*" ? "0.0.0.0" : address;
        }
    }
}
