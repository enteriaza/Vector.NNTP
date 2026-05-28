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
        private readonly ProxyTrustedSource[] _trustedProxySources;

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
            _trustedProxySources = ParseTrustedProxySources(_options.Value.ProxyProtocolTrustedSources);
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

        /// <summary>
        /// Runs a listener for the given bind address and port.
        /// </summary>
        /// <param name="bindAddress">The bind address.</param>
        /// <param name="port">The port.</param>
        /// <param name="implicitTls">Whether implicit TLS is enabled.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the listener stops.</returns>
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

        /// <summary>
        /// Tries to acquire a connection slot for the given client endpoint.
        /// </summary>
        /// <param name="clientEndPoint">The client endpoint.</param>
        /// <returns>True if a slot was acquired, false otherwise.</returns>
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

        /// <summary>
        /// Releases a connection slot for the given client endpoint.
        /// </summary>
        /// <param name="clientEndPoint">The client endpoint.</param>
        private void ReleaseConnectionSlot(IPEndPoint clientEndPoint)
        {
            _ = Interlocked.Decrement(ref _activeConnections);
            if (_options.Value.MaxConnectionsPerClientIp > 0)
            {
                DecrementPerIp(clientEndPoint.Address.ToString());
            }
        }

        /// <summary>
        /// Tries to increment the connection count for the given client endpoint.
        /// </summary>
        /// <param name="ipKey">The client endpoint key.</param>
        /// <param name="limit">The limit.</param>
        /// <returns>True if the count was incremented, false otherwise.</returns>
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

        /// <summary>
        /// Decrements the connection count for the given client endpoint.
        /// </summary>
        /// <param name="ipKey">The client endpoint key.</param>
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

        /// <summary>
        /// Handles a connection for the given socket.
        /// </summary>
        /// <param name="socket">The socket.</param>
        /// <param name="implicitTls">Whether implicit TLS is enabled.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the connection is handled.</returns>
        private async Task HandleConnectionAsync(Socket socket, bool implicitTls, CancellationToken cancellationToken)
        {
            _inFlight.Enter();
            IPEndPoint clientEndPoint = (IPEndPoint)socket.RemoteEndPoint!;
            bool slotAcquired = false;
            try
            {
                IPEndPoint tcpPeer = clientEndPoint;
                string sessionId = Guid.NewGuid().ToString("N");
                Stream baseStream = new NetworkStream(socket, ownsSocket: false);
                ReadOnlyMemory<byte> replayPrefix = ReadOnlyMemory<byte>.Empty;

                if (_options.Value.EnableProxyProtocol)
                {
                    (IPEndPoint proxiedClient, ReadOnlyMemory<byte> remainder, bool consumedProxy) =
                        await ReadProxyPreambleAsync(baseStream, tcpPeer, cancellationToken).ConfigureAwait(false);
                    if (consumedProxy)
                    {
                        if (_options.Value.ProxyProtocolStrictTrustedSourcesOnly && !IsTrustedProxyHop(tcpPeer.Address))
                        {
                            socket.Dispose();
                            return;
                        }

                        clientEndPoint = proxiedClient;
                    }

                    replayPrefix = remainder;
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
                    Stream tlsStream = replayPrefix.Length > 0
                        ? new PrefixedReadStream(baseStream, replayPrefix, leaveInnerOpen: false)
                        : baseStream;
                    SslStream ssl = new(tlsStream, leaveInnerStreamOpen: false);
                    await NntpTlsHandshake.AuthenticateServerAsync(ssl, cert, cancellationToken).ConfigureAwait(false);
                    NntpSocketTransport transport = new(socket, ssl);
                    await _runner.RunAsync(transport, context, tlsAlreadyActive: true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    NntpConnectionContext context = new(sessionId, clientEndPoint, tcpPeer, _profile.Role);
                    Stream sessionStream = replayPrefix.Length > 0
                        ? new PrefixedReadStream(baseStream, replayPrefix, leaveInnerOpen: false)
                        : baseStream;
                    NntpSocketTransport transport = new(socket, sessionStream);
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

        /// <summary>
        /// Reads and parses a PROXY protocol preamble (v1/v2) from the stream.
        /// </summary>
        /// <param name="stream">Underlying stream.</param>
        /// <param name="tcpPeer">TCP peer endpoint.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Proxied client, remainder bytes already read, and whether a preamble was consumed.</returns>
        private static async Task<(IPEndPoint ClientEndPoint, ReadOnlyMemory<byte> Remainder, bool ConsumedProxy)> ReadProxyPreambleAsync(
            Stream stream,
            IPEndPoint tcpPeer,
            CancellationToken cancellationToken)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(ProxyProtocolPreamble.MaxV2FrameLength + 32);
            try
            {
                int total = 0;
                int max = rented.Length;
                while (total < max)
                {
                    int read = await stream.ReadAsync(rented.AsMemory(total, max - total), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;

                    if (ProxyProtocolPreamble.TryParse(rented, total, tcpPeer, out int consumed, out IPEndPoint client))
                    {
                        if (consumed <= 0)
                        {
                            return (tcpPeer, rented.AsMemory(0, total).ToArray(), false);
                        }

                        ReadOnlyMemory<byte> remainder = consumed < total
                            ? rented.AsMemory(consumed, total - consumed).ToArray()
                            : ReadOnlyMemory<byte>.Empty;
                        return (client, remainder, consumed > 0);
                    }

                    if (total >= ProxyProtocolPreamble.MaxV1LineBytes && Array.IndexOf(rented, (byte)'\n', 0, total) < 0)
                    {
                        return (tcpPeer, rented.AsMemory(0, total).ToArray(), false);
                    }
                }

                return (tcpPeer, rented.AsMemory(0, total).ToArray(), false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static ProxyTrustedSource[] ParseTrustedProxySources(string[] sources)
        {
            if (sources is null || sources.Length == 0)
            {
                return Array.Empty<ProxyTrustedSource>();
            }

            List<ProxyTrustedSource> parsed = new();
            foreach (string entry in sources)
            {
                if (ProxyTrustedSource.TryParse(entry, out ProxyTrustedSource source))
                {
                    parsed.Add(source);
                }
            }

            return parsed.ToArray();
        }

        private bool IsTrustedProxyHop(IPAddress address)
        {
            if (_trustedProxySources.Length == 0)
            {
                return false;
            }

            foreach (ProxyTrustedSource src in _trustedProxySources)
            {
                if (src.Contains(address))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Handles the certificate changed event.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        private void OnCertificateChanged(X509Certificate2 certificate)
        {
            _ = Interlocked.Exchange(ref _handshakeCertificate, certificate);
            LogTlsCertificateUpdated(certificate.Thumbprint);
        }

        /// <summary>
        /// Disposes the acceptor.
        /// </summary>
        public void Dispose()
        {
            if (_renewalService is not null)
            {
                _renewalService.CertificateChanged -= OnCertificateChanged;
            }
        }

        /// <summary>
        /// Normalizes the bind address.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>The normalized address.</returns>
        private static string NormalizeBind(string address)
        {
            return string.IsNullOrWhiteSpace(address) || address == "*" ? "0.0.0.0" : address;
        }
    }
}
