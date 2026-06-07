// <copyright file="CertificateClusterSync.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Telemetry;
using Vector.NNTP.Utilities.IO;
using Vector.NNTP.Utilities.Security;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Consuming;
using Vector.NNTP.MessageBus.Publishing;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Coordinates multi-node certificate distribution via RabbitMQ fanout and a best-effort leader lock.
    /// </summary>
    /// <remarks>
    /// <para><b>Logging:</b> <see cref="LoggerMessageAttribute"/> partial methods in
    /// <c>CertificateClusterSync.Logging.cs</c>.</para>
    /// </remarks>
    /// <param name="logger">Logger scoped to the renewal service.</param>
    /// <param name="getOptions">Accessor for current Let's Encrypt options.</param>
    /// <param name="hostEnvironment">Hosting environment for exchange/queue naming.</param>
    /// <param name="connectionFactory">RabbitMQ connection factory for topology and leader lock.</param>
    /// <param name="rabbitOptions">RabbitMQ connection options.</param>
    /// <param name="publisherPool">Publisher pool for fanout broadcasts.</param>
    /// <param name="consumerManager">Long-lived consumer manager for follower adoption.</param>
    /// <param name="store">Local certificate persistence.</param>
    /// <param name="activateCertificateAsync">Callback to activate an adopted certificate for TLS.</param>
    /// <param name="metrics">Optional metrics recorder for cluster message outcomes.</param>
    internal sealed partial class CertificateClusterSync(
        ILogger logger,
        Func<LetsEncryptOptions> getOptions,
        IHostEnvironment hostEnvironment,
        IRabbitMqConnectionFactory connectionFactory,
        RabbitMQOptions rabbitOptions,
        IRabbitMqPublisherPool publisherPool,
        IRabbitMqConsumerManager consumerManager,
        CertificateStore store,
        Func<X509Certificate2, Task> activateCertificateAsync,
        EncryptionMetrics? metrics = null) : IAsyncDisposable
    {
        /// <summary>
        /// Wire payload type for cluster certificate broadcasts.
        /// </summary>
        internal const string ClusterPayloadType = "vector.nntp.certificate.cluster.v1";

        /// <summary>
        /// The maximum future skew for the cluster certificate issued at UTC timestamp.
        /// </summary>
        private static readonly TimeSpan ClusterIssuedAtMaxFutureSkew = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The maximum past age for the cluster certificate issued at UTC timestamp.
        /// </summary>
        private static readonly TimeSpan ClusterIssuedAtMaxPastAge = TimeSpan.FromDays(7);

        /// <summary>
        /// The line separators for the cluster adoption state file.
        /// </summary>
        private static readonly char[] ClusterAdoptionLineSeparators = ['\r', '\n'];

        /// <summary>
        /// Accessor for current <see cref="LetsEncryptOptions"/> (cluster exchange names, signing secrets, cert dir).
        /// </summary>
        private readonly Func<LetsEncryptOptions> _getOptions = getOptions;

        /// <summary>
        /// Hosting environment for exchange/queue name segments and leader-lock queue naming.
        /// </summary>
        private readonly IHostEnvironment _hostEnvironment = hostEnvironment;

        /// <summary>
        /// RabbitMQ connection factory for topology declaration and exclusive leader-lock channels.
        /// </summary>
        private readonly IRabbitMqConnectionFactory _connectionFactory = connectionFactory;

        /// <summary>
        /// RabbitMQ connection options passed to factory and publisher pool scopes.
        /// </summary>
        private readonly RabbitMQOptions _rabbitOptions = rabbitOptions;

        /// <summary>
        /// Publisher pool used to fan out signed certificate payloads to the cluster exchange.
        /// </summary>
        private readonly IRabbitMqPublisherPool _publisherPool = publisherPool;

        /// <summary>
        /// Long-lived consumer manager that delivers follower adoption messages to <see cref="OnClusterMessageAsync"/>.
        /// </summary>
        private readonly IRabbitMqConsumerManager _consumerManager = consumerManager;

        /// <summary>
        /// Local PFX persistence for adopted cluster certificates.
        /// </summary>
        private readonly CertificateStore _store = store;

        /// <summary>
        /// Host callback that installs an adopted certificate for live TLS handshakes.
        /// </summary>
        private readonly Func<X509Certificate2, Task> _activateCertificateAsync = activateCertificateAsync;

        /// <summary>
        /// Highest certificate epoch accepted locally; stale fanout messages with lower epochs are acked and ignored.
        /// </summary>
        private long _lastAcceptedEpoch;

        /// <summary>
        /// One-shot startup guard (<c>0</c> = not started, <c>1</c> = consumer topology established).
        /// </summary>
        private int _started;

        /// <summary>
        /// Loads persisted adoption state and starts the fanout consumer when cluster mode is enabled.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown token.</param>
        /// <returns>A task that completes after adoption state is loaded and the fanout consumer is registered (or skipped when already started).</returns>
        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            LetsEncryptOptions options = _getOptions();
            (_lastAcceptedEpoch, _) = await ReadClusterAdoptionStateAsync(options, cancellationToken).ConfigureAwait(false);

            try
            {
                await EnsureTopologyAndConsumerAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogClusterConsumerStartFailed(ex);
            }
        }

        /// <summary>
        /// Attempts ACME renewal only when this node holds the exclusive leader queue.
        /// </summary>
        /// <param name="issueAsync">Issuance delegate invoked while the leader lock is held.</param>
        /// <param name="cancellationToken">Host shutdown token.</param>
        /// <returns>
        /// A task that completes when the exclusive leader queue cannot be acquired, the leader connection shuts down,
        /// or <paramref name="issueAsync"/> finishes while the lock is held.
        /// </returns>
        internal async Task TryRenewAsLeaderAsync(Func<CancellationToken, Task> issueAsync, CancellationToken cancellationToken)
        {
            IConnection leaderConnection = await _connectionFactory
                .CreateConnectionAsync(_rabbitOptions, cancellationToken)
                .ConfigureAwait(false);
            await using (leaderConnection.ConfigureAwait(false))
            {
                IChannel leaderChannel = await leaderConnection.CreateChannelAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await using (leaderChannel.ConfigureAwait(false))
                {
                    string leaderQueue = $"vectornntp.acme.leader.{SanitizeSegment(_hostEnvironment.EnvironmentName)}";
                    try
                    {
                        _ = await leaderChannel.QueueDeclareAsync(
                            queue: leaderQueue,
                            durable: false,
                            exclusive: true,
                            autoDelete: true,
                            arguments: null,
                            passive: false,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogNotAcmeLeader(ex, leaderQueue);
                        return;
                    }

                    LogAcmeLeaderAcquired(leaderQueue);
                    using CancellationTokenSource connectionShutdownCts = new();
                    Task OnLeaderConnectionShutdown(object? sender, ShutdownEventArgs shutdownArgs)
                    {
                        _ = sender;
                        _ = shutdownArgs;
                        try { connectionShutdownCts.Cancel(); }
                        catch (ObjectDisposedException)
                        {
                            LogLeaderConnectionCtsDisposed();
                        }

                        return Task.CompletedTask;
                    }

                    leaderConnection.ConnectionShutdownAsync += OnLeaderConnectionShutdown;
                    try
                    {
                        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            connectionShutdownCts.Token);
                        await issueAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        leaderConnection.ConnectionShutdownAsync -= OnLeaderConnectionShutdown;
                    }
                }
            }
        }

        /// <summary>
        /// Publishes a newly issued certificate to the cluster fanout exchange and records local adoption state.
        /// </summary>
        /// <param name="certificate">Issued certificate with private key.</param>
        /// <param name="pfxBytes">Exported PFX bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="correlationId">Optional renewal correlation id propagated to cluster subscribers via MessageBus headers.</param>
        /// <returns>A task that completes after optional fanout publish and local adoption state are persisted.</returns>
        internal async Task PublishAndRecordAsync(
            X509Certificate2 certificate,
            byte[] pfxBytes,
            CancellationToken cancellationToken,
            string? correlationId = null)
        {
            using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                "encryption.cluster.publish",
                ActivityKind.Producer);
            _ = activity?.SetTag("encryption.thumbprint", certificate.Thumbprint);

            LetsEncryptOptions options = _getOptions();
            long epoch = await IncrementEpochAsync(options, cancellationToken).ConfigureAwait(false);
            string sha256Hex = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            string[] orderDomains = CertificateOrderDomainBuilder.BuildOrderDomains(options);

            if (options.ClusterEnabled)
            {
                ClusterCertificatePayload dto = new()
                {
                    Epoch = epoch,
                    SignatureVersion = ClusterCertificatePayload.CurrentSignatureVersion,
                    PfxBase64 = Convert.ToBase64String(pfxBytes),
                    Sha256Thumbprint = sha256Hex,
                    Domains = orderDomains,
                    NotAfterUtcTicks = certificate.NotAfter.ToUniversalTime().Ticks,
                    IssuedAtUtcTicks = DateTime.UtcNow.Ticks,
                };

                byte[]? signingSecret = GetClusterSigningSecretUtf8(options.ClusterBroadcastSigningSecret);
                try
                {
                    if (signingSecret is not null)
                        dto.Signature = ClusterCertificatePayloadHmac.ComputeSignature(dto, signingSecret);
                }
                finally
                {
                    SecureMemoryUtilities.ZeroBuffers(signingSecret);
                }

                ClusterBusEnvelope envelope = new()
                {
                    SchemaVersion = 1,
                    PayloadType = ClusterPayloadType,
                    Payload = dto,
                };

                byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope, ClusterJsonContext.Default.ClusterBusEnvelope);
                string exchange = ResolveBroadcastExchangeName(options);
                IPublisherScope scope = await _publisherPool.CreateScopeAsync(cancellationToken).ConfigureAwait(false);
                await using (scope.ConfigureAwait(false))
                {
                    await scope.PublishAsync(exchange, string.Empty, body, correlationId, cancellationToken).ConfigureAwait(false);
                }

                LogClusterCertificatePublished(epoch, exchange);
                metrics?.RecordClusterMessage("published");
            }

            await RecordClusterAdoptionAsync(options, epoch, sha256Hex, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases cluster sync resources; consumer lifetime is owned by <see cref="IRabbitMqConsumerManager"/>.
        /// </summary>
        /// <returns>A completed <see cref="ValueTask"/> (no asynchronous teardown required today).</returns>
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Declares the fanout exchange, per-node quorum queue, binding, and registers the adoption consumer.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying <see cref="LetsEncryptOptions.ClusterBroadcastExchange"/>.</param>
        /// <param name="cancellationToken">Host shutdown token for RabbitMQ I/O.</param>
        /// <returns>A task that completes when the consumer subscription is active.</returns>
        private async Task EnsureTopologyAndConsumerAsync(LetsEncryptOptions options, CancellationToken cancellationToken)
        {
            string exchange = ResolveBroadcastExchangeName(options);
            string queueName = $"vectornntp.cert.node.{SanitizeSegment(Environment.MachineName)}.{SanitizeSegment(_hostEnvironment.EnvironmentName)}";

            IConnection connection = await _connectionFactory.CreateConnectionAsync(_rabbitOptions, cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    await channel.ExchangeDeclareAsync(
                        exchange: exchange,
                        type: ExchangeType.Fanout,
                        durable: true,
                        autoDelete: false,
                        arguments: null,
                        passive: false,
                        noWait: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    Dictionary<string, object?> queueArgs = new() { ["x-queue-type"] = "quorum" };
                    _ = await channel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: queueArgs,
                        passive: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    await channel.QueueBindAsync(queue: queueName, exchange: exchange, routingKey: string.Empty, arguments: null, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    _ = await _consumerManager.RegisterSubscriptionAsync(queueName, OnClusterMessageAsync, cancellationToken)
                        .ConfigureAwait(false);
                    LogClusterConsumerBound(queueName, exchange);
                }
            }
        }

        /// <summary>
        /// Validates, verifies, and optionally adopts a cluster certificate fanout message.
        /// </summary>
        /// <param name="sender">RabbitMQ <see cref="AsyncEventingBasicConsumer"/> delivering <paramref name="args"/>.</param>
        /// <param name="args">Delivered message body and delivery tag for ack/nack.</param>
        /// <returns>A task that completes after the message is acked, nacked, or ignored.</returns>
        /// <remarks>
        /// Invalid envelopes, HMAC failures, domain mismatches, and expired certificates are nacked without requeue.
        /// Superseded epochs and foreign payload types are acked silently.
        /// </remarks>
        private async Task OnClusterMessageAsync(object sender, BasicDeliverEventArgs args)
        {
            _ = sender;
            using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                "encryption.cluster.consume",
                ActivityKind.Consumer);

            IChannel? channel = args.BasicProperties?.Headers is null ? null : (sender as AsyncEventingBasicConsumer)?.Channel;
            try
            {
                ReadOnlyMemory<byte> body = args.Body;
                if (!ClusterEnvelopeEpochPrefilter.TryReadEnvelopePayloadTypeAndClusterEpoch(
                        body.Span,
                        out string? wirePayloadType,
                        out bool epochPresent,
                        out long dtoEpoch,
                        logger))
                {
                    LogClusterInvalidEnvelope();
                    metrics?.RecordClusterMessage("rejected");
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(wirePayloadType, ClusterPayloadType, StringComparison.Ordinal))
                {
                    if (channel is not null)
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false).ConfigureAwait(false);
                    return;
                }

                if (epochPresent && dtoEpoch <= _lastAcceptedEpoch)
                {
                    if (channel is not null)
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false).ConfigureAwait(false);
                    return;
                }

                ClusterBusEnvelope? envelope = JsonSerializer.Deserialize(body.Span, ClusterJsonContext.Default.ClusterBusEnvelope);
                ClusterCertificatePayload? dto = envelope?.Payload;
                if (dto is null || !string.Equals(envelope?.PayloadType, ClusterPayloadType, StringComparison.Ordinal))
                {
                    LogClusterInvalidPayload();
                    metrics?.RecordClusterMessage("rejected");
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                if (dto.Epoch <= _lastAcceptedEpoch)
                {
                    if (channel is not null)
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false).ConfigureAwait(false);
                    return;
                }

                LetsEncryptOptions options = _getOptions();
                if (!TryVerifyClusterCertificateSignature(dto, options))
                {
                    LogClusterHmacVerificationFailed();
                    metrics?.RecordClusterMessage("invalid_hmac");
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                string[] expectedDomains = CertificateOrderDomainBuilder.BuildOrderDomains(options);
                if (!ClusterCertificateDomainBinding.OrderDomainsMatch(expectedDomains, dto.Domains))
                {
                    LogClusterDomainMismatch();
                    metrics?.RecordClusterMessage("rejected");
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                byte[] pfxBytes;
                try
                {
                    pfxBytes = Convert.FromBase64String(dto.PfxBase64);
                }
                catch (FormatException ex)
                {
                    LogClusterInvalidPfxBase64(ex);
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                string? pwd = options.PfxExportPassword;
                X509Certificate2 cert = new(pfxBytes, pwd, CertificateDefaults.PfxKeyStorageFlags);
                try
                {
                    if (cert.NotAfter <= DateTime.UtcNow)
                    {
                        LogClusterCertificateExpired();
                        if (channel is not null)
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                        return;
                    }

                    if (cert.NotAfter.ToUniversalTime().Ticks != dto.NotAfterUtcTicks)
                    {
                        LogClusterExpiryMetadataMismatch();
                        if (channel is not null)
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                        return;
                    }

                    string candidateSha256 = Convert.ToHexString(SHA256.HashData(cert.RawData));
                    if (!string.Equals(candidateSha256, dto.Sha256Thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        LogClusterFingerprintMismatch();
                        if (channel is not null)
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                        return;
                    }

                    DateTime issuedAtUtc = new(dto.IssuedAtUtcTicks, DateTimeKind.Utc);
                    DateTime utcNow = DateTime.UtcNow;
                    if (issuedAtUtc > utcNow + ClusterIssuedAtMaxFutureSkew)
                    {
                        LogClusterIssuedAtTooFarInFuture();
                        if (channel is not null)
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                        return;
                    }

                    if (issuedAtUtc < utcNow - ClusterIssuedAtMaxPastAge)
                    {
                        LogClusterIssuedAtTooOld();
                        if (channel is not null)
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                        return;
                    }

                    await _store.SaveCertificateAsync(pfxBytes, CancellationToken.None).ConfigureAwait(false);
                    await _activateCertificateAsync(cert).ConfigureAwait(false);
                    cert = null!;

                    await RecordClusterAdoptionAsync(options, dto.Epoch, candidateSha256, CancellationToken.None).ConfigureAwait(false);
                    LogClusterCertificateAdopted(dto.Epoch);
                    metrics?.RecordClusterMessage("accepted");
                    _ = activity?.SetTag("encryption.thumbprint", candidateSha256);
                }
                finally
                {
                    cert?.Dispose();
                }

                if (channel is not null)
                    await channel.BasicAckAsync(args.DeliveryTag, multiple: false).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metrics?.RecordClusterMessage("rejected");
                LogClusterMessageHandlingFailed(ex);
                if (sender is AsyncEventingBasicConsumer consumer && consumer.Channel.IsOpen)
                    await consumer.Channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds the environment-scoped fanout exchange name from configuration and hosting environment.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying the exchange prefix.</param>
        /// <returns><c>{ClusterBroadcastExchange}.{environment}</c> with non-alphanumeric segments normalised.</returns>
        private string ResolveBroadcastExchangeName(LetsEncryptOptions options)
        {
            string prefix = options.ClusterBroadcastExchange.Trim();
            string env = SanitizeSegment(_hostEnvironment.EnvironmentName);
            return $"{prefix}.{env}";
        }

        /// <summary>
        /// Normalises a queue or exchange name segment to lowercase ASCII letters, digits, or underscores.
        /// </summary>
        /// <param name="value">Raw environment or machine name segment.</param>
        /// <returns>Sanitised segment, or <c>default</c> when blank.</returns>
        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "default";

            char[] chars = value.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsAsciiLetterOrDigit(chars[i]))
                    chars[i] = '_';
            }

            return new string(chars);
        }

        /// <summary>
        /// Resolves the on-disk path for persisted cluster adoption epoch and thumbprint metadata.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying <see cref="LetsEncryptOptions.CertDir"/>.</param>
        /// <returns>Absolute path to <c>cluster-adoption.state</c> under the certificate directory.</returns>
        private static string ClusterAdoptionStatePath(LetsEncryptOptions options)
        {
            return Path.Combine(Path.GetFullPath(options.CertDir), "cluster-adoption.state");
        }

        /// <summary>
        /// Reads the last accepted epoch and SHA-256 certificate fingerprint from local adoption state.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying the certificate directory.</param>
        /// <param name="cancellationToken">Cancellation token for resilient file read.</param>
        /// <returns>
        /// Parsed epoch and thumbprint, or <c>(0, string.Empty)</c> when the state file is missing or malformed.
        /// </returns>
        private static async Task<(long Epoch, string Sha256Thumbprint)> ReadClusterAdoptionStateAsync(
            LetsEncryptOptions options,
            CancellationToken cancellationToken)
        {
            string path = ClusterAdoptionStatePath(options);
            string? text = await FileIOUtilities.TryReadFileAsync(
                static (p, ct) => File.ReadAllTextAsync(p, ct),
                path,
                null,
                cancellationToken).ConfigureAwait(false);
            if (text is null)
                return (0, string.Empty);

            string[] lines = text.Split(ClusterAdoptionLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2 || !long.TryParse(lines[0], out long epoch))
                return (0, string.Empty);

            return (epoch, lines[1]);
        }

        /// <summary>
        /// Allocates the next monotonic certificate epoch for a leader publish.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying the certificate directory.</param>
        /// <param name="cancellationToken">Cancellation token for adoption state read.</param>
        /// <returns><c>lastEpoch + 1</c> based on persisted adoption state.</returns>
        private static async Task<long> IncrementEpochAsync(LetsEncryptOptions options, CancellationToken cancellationToken)
        {
            (long current, _) = await ReadClusterAdoptionStateAsync(options, cancellationToken).ConfigureAwait(false);
            return current + 1;
        }

        /// <summary>
        /// Atomically persists adoption epoch and thumbprint and updates <see cref="_lastAcceptedEpoch"/>.
        /// </summary>
        /// <param name="options">Let's Encrypt options supplying the certificate directory.</param>
        /// <param name="epoch">Accepted certificate epoch.</param>
        /// <param name="sha256Hex">SHA-256 hex digest of the adopted certificate raw bytes.</param>
        /// <param name="cancellationToken">Cancellation token for atomic file write.</param>
        /// <returns>A task that completes when adoption state is written.</returns>
        private async Task RecordClusterAdoptionAsync(LetsEncryptOptions options, long epoch, string sha256Hex, CancellationToken cancellationToken)
        {
            string body = $"{epoch}{Environment.NewLine}{sha256Hex}{Environment.NewLine}";
            await FileIOUtilities.AtomicWriteAsync(ClusterAdoptionStatePath(options), Encoding.UTF8.GetBytes(body), cancellationToken)
                .ConfigureAwait(false);
            _lastAcceptedEpoch = epoch;
        }

        /// <summary>
        /// Verifies the HMAC signature on a cluster certificate payload using current and optional previous secrets.
        /// </summary>
        /// <param name="dto">Deserialised cluster certificate payload.</param>
        /// <param name="options">Let's Encrypt options supplying broadcast signing secrets.</param>
        /// <returns>
        /// <see langword="true"/> when no secrets are configured and the payload has no signature, or when the
        /// signature matches the current or previous secret; otherwise <see langword="false"/>.
        /// </returns>
        private static bool TryVerifyClusterCertificateSignature(ClusterCertificatePayload dto, LetsEncryptOptions options)
        {
            byte[]? current = GetClusterSigningSecretUtf8(options.ClusterBroadcastSigningSecret);
            byte[]? previous = GetClusterSigningSecretUtf8(options.ClusterBroadcastSigningSecretPrevious);
            try
            {
                return current is null && previous is null
                    ? string.IsNullOrEmpty(dto.Signature)
                    : (current is not null && ClusterCertificatePayloadHmac.IsSignatureValid(dto, current, dto.Signature))
                      || (previous is not null && ClusterCertificatePayloadHmac.IsSignatureValid(dto, previous, dto.Signature));
            }
            finally
            {
                SecureMemoryUtilities.ZeroBuffers(current, previous);
            }
        }

        /// <summary>
        /// Materialises a trimmed cluster signing secret as UTF-8 bytes for HMAC verification.
        /// </summary>
        /// <param name="secret">Configured signing secret, or <see langword="null"/>/whitespace when unset.</param>
        /// <returns>UTF-8 secret bytes, or <see langword="null"/> when <paramref name="secret"/> is blank.</returns>
        /// <remarks>Callers must zero returned buffers via <c>SecureMemoryUtilities.ZeroBuffers</c>.</remarks>
        private static byte[]? GetClusterSigningSecretUtf8(string? secret)
        {
            return string.IsNullOrWhiteSpace(secret) ? null : Encoding.UTF8.GetBytes(secret.Trim());
        }
    }
}
