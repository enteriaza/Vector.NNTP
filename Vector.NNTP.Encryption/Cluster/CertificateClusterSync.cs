// <copyright file="CertificateClusterSync.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
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
    internal sealed partial class CertificateClusterSync : IAsyncDisposable
    {
        /// <summary>
        /// Wire payload type for cluster certificate broadcasts.
        /// </summary>
        internal const string ClusterPayloadType = "vector.nntp.certificate.cluster.v1";

        private static readonly TimeSpan ClusterIssuedAtMaxFutureSkew = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ClusterIssuedAtMaxPastAge = TimeSpan.FromDays(7);
        private static readonly char[] ClusterAdoptionLineSeparators = ['\r', '\n'];

        private readonly ILogger _logger;

        /// <summary>
        /// Gets the logger instance for source-generated <see cref="LoggerMessageAttribute"/> methods.
        /// </summary>
        private ILogger Logger => _logger;
        private readonly Func<LetsEncryptOptions> _getOptions;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly RabbitMqConnectionFactory _connectionFactory;
        private readonly RabbitMQOptions _rabbitOptions;
        private readonly IRabbitMqPublisherPool _publisherPool;
        private readonly IRabbitMqConsumerManager _consumerManager;
        private readonly CertificateStore _store;
        private readonly Func<X509Certificate2, Task> _activateCertificateAsync;

        private long _lastAcceptedEpoch;
        private string _lastAcceptedSha256 = string.Empty;
        private Guid _consumerSubscriptionId;
        private int _started;

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateClusterSync"/> class.
        /// </summary>
        /// <param name="logger">Logger scoped to the renewal service.</param>
        /// <param name="getOptions">Accessor for current Let's Encrypt options.</param>
        /// <param name="hostEnvironment">Hosting environment for exchange/queue naming.</param>
        /// <param name="connectionFactory">RabbitMQ connection factory for topology and leader lock.</param>
        /// <param name="rabbitOptions">RabbitMQ connection options.</param>
        /// <param name="publisherPool">Publisher pool for fanout broadcasts.</param>
        /// <param name="consumerManager">Long-lived consumer manager for follower adoption.</param>
        /// <param name="store">Local certificate persistence.</param>
        /// <param name="activateCertificateAsync">Callback to activate an adopted certificate for TLS.</param>
        public CertificateClusterSync(
            ILogger logger,
            Func<LetsEncryptOptions> getOptions,
            IHostEnvironment hostEnvironment,
            RabbitMqConnectionFactory connectionFactory,
            RabbitMQOptions rabbitOptions,
            IRabbitMqPublisherPool publisherPool,
            IRabbitMqConsumerManager consumerManager,
            CertificateStore store,
            Func<X509Certificate2, Task> activateCertificateAsync)
        {
            _logger = logger;
            _getOptions = getOptions;
            _hostEnvironment = hostEnvironment;
            _connectionFactory = connectionFactory;
            _rabbitOptions = rabbitOptions;
            _publisherPool = publisherPool;
            _consumerManager = consumerManager;
            _store = store;
            _activateCertificateAsync = activateCertificateAsync;
        }

        /// <summary>
        /// Loads persisted adoption state and starts the fanout consumer when cluster mode is enabled.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown token.</param>
        /// <returns>A task that completes when startup finishes or is skipped.</returns>
        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            LetsEncryptOptions options = _getOptions();
            (_lastAcceptedEpoch, _lastAcceptedSha256) = await ReadClusterAdoptionStateAsync(options, cancellationToken).ConfigureAwait(false);

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
        /// <returns>A task that completes when this node is not leader or issuance finishes.</returns>
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
                await leaderChannel.QueueDeclareAsync(
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
                catch (ObjectDisposedException) { }

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
        /// <returns>A task that completes when publish and local state update finish.</returns>
        internal async Task PublishAndRecordAsync(X509Certificate2 certificate, byte[] pfxBytes, CancellationToken cancellationToken)
        {
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
                if (signingSecret is not null)
                    dto.Signature = ClusterCertificatePayloadHmac.ComputeSignature(dto, signingSecret);

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
                    await scope.PublishAsync(exchange, string.Empty, body, cancellationToken).ConfigureAwait(false);
                }

                LogClusterCertificatePublished(epoch, exchange);
            }

            await RecordClusterAdoptionAsync(options, epoch, sha256Hex, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs,
                passive: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueBindAsync(queue: queueName, exchange: exchange, routingKey: string.Empty, arguments: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _consumerSubscriptionId = await _consumerManager.RegisterSubscriptionAsync(queueName, OnClusterMessageAsync, cancellationToken)
                .ConfigureAwait(false);
            LogClusterConsumerBound(queueName, exchange);
            }
            }
        }

        private async Task OnClusterMessageAsync(object sender, BasicDeliverEventArgs args)
        {
            _ = sender;
            IChannel? channel = args.BasicProperties?.Headers is null ? null : (sender as AsyncEventingBasicConsumer)?.Channel;
            try
            {
                ReadOnlyMemory<byte> body = args.Body;
                if (!ClusterEnvelopeEpochPrefilter.TryReadEnvelopePayloadTypeAndClusterEpoch(body.Span, out string? wirePayloadType, out bool epochPresent, out long dtoEpoch))
                {
                    LogClusterInvalidEnvelope();
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
                    if (channel is not null)
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
                    return;
                }

                string[] expectedDomains = CertificateOrderDomainBuilder.BuildOrderDomains(options);
                if (!ClusterCertificateDomainBinding.OrderDomainsMatch(expectedDomains, dto.Domains))
                {
                    LogClusterDomainMismatch();
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
                    _lastAcceptedEpoch = dto.Epoch;
                    _lastAcceptedSha256 = candidateSha256;
                    LogClusterCertificateAdopted(dto.Epoch);
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
                LogClusterMessageHandlingFailed(ex);
                if (sender is AsyncEventingBasicConsumer consumer && consumer.Channel.IsOpen)
                    await consumer.Channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
            }
        }

        private string ResolveBroadcastExchangeName(LetsEncryptOptions options)
        {
            string prefix = options.ClusterBroadcastExchange.Trim();
            string env = SanitizeSegment(_hostEnvironment.EnvironmentName);
            return $"{prefix}.{env}";
        }

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

        private static string ClusterAdoptionStatePath(LetsEncryptOptions options) =>
            Path.Combine(Path.GetFullPath(options.CertDir), "cluster-adoption.state");

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

        private static async Task<long> IncrementEpochAsync(LetsEncryptOptions options, CancellationToken cancellationToken)
        {
            (long current, _) = await ReadClusterAdoptionStateAsync(options, cancellationToken).ConfigureAwait(false);
            return current + 1;
        }

        private async Task RecordClusterAdoptionAsync(LetsEncryptOptions options, long epoch, string sha256Hex, CancellationToken cancellationToken)
        {
            string body = $"{epoch}{Environment.NewLine}{sha256Hex}{Environment.NewLine}";
            await FileIOUtilities.AtomicWriteAsync(ClusterAdoptionStatePath(options), Encoding.UTF8.GetBytes(body), cancellationToken)
                .ConfigureAwait(false);
            _lastAcceptedEpoch = epoch;
            _lastAcceptedSha256 = sha256Hex;
        }

        private static bool TryVerifyClusterCertificateSignature(ClusterCertificatePayload dto, LetsEncryptOptions options)
        {
            byte[]? current = GetClusterSigningSecretUtf8(options.ClusterBroadcastSigningSecret);
            byte[]? previous = GetClusterSigningSecretUtf8(options.ClusterBroadcastSigningSecretPrevious);

            if (current is null && previous is null)
                return string.IsNullOrEmpty(dto.Signature);

            if (current is not null && ClusterCertificatePayloadHmac.IsSignatureValid(dto, current, dto.Signature))
                return true;

            return previous is not null && ClusterCertificatePayloadHmac.IsSignatureValid(dto, previous, dto.Signature);
        }

        private static byte[]? GetClusterSigningSecretUtf8(string? secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
                return null;

            return Encoding.UTF8.GetBytes(secret.Trim());
        }
    }
}
