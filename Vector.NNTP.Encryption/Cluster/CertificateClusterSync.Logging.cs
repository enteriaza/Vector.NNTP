// <copyright file="CertificateClusterSync.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateClusterSync.Logging.cs -- Source-generated [LoggerMessage] partial methods for CertificateClusterSync.

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="CertificateClusterSync"/>.
    /// </summary>
    internal sealed partial class CertificateClusterSync
    {
        #region Logging -- Cluster Sync (310-329)

        /// <summary>
        /// Logs that the cluster certificate consumer could not be started; continuing without live fanout sync.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        [LoggerMessage(EventId = 310, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate consumer could not be started; continuing without live fanout sync")]
        private partial void LogClusterConsumerStartFailed(Exception ex);

        /// <summary>
        /// Logs that this node is not the ACME leader (queue {Queue} is held elsewhere).
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="queue">The queue that is held elsewhere.</param>
        [LoggerMessage(EventId = 311, Level = LogLevel.Information,
            Message = "Certificates: This node is not the ACME leader (queue {Queue} is held elsewhere)")]
        private partial void LogNotAcmeLeader(Exception ex, string queue);

        /// <summary>
        /// Logs that this node has acquired the ACME leader lock on queue {Queue}.
        /// </summary>
        /// <param name="queue">The queue that the node has acquired the lock on.</param>
        [LoggerMessage(EventId = 312, Level = LogLevel.Information,
            Message = "Certificates: Acquired ACME leader lock on queue {Queue}")]
        private partial void LogAcmeLeaderAcquired(string queue);

        /// <summary>
        /// Logs that the cluster certificate epoch {Epoch} has been published to exchange {Exchange}.
        /// </summary>
        /// <param name="epoch">The epoch that has been published.</param>
        /// <param name="exchange">The exchange that the epoch has been published to.</param>
        [LoggerMessage(EventId = 313, Level = LogLevel.Information,
            Message = "Certificates: Published cluster certificate epoch {Epoch} to exchange {Exchange}")]
        private partial void LogClusterCertificatePublished(long epoch, string exchange);

        /// <summary>
        /// Logs that the cluster certificate consumer has been bound to queue {Queue} on exchange {Exchange}.
        /// </summary>
        /// <param name="queue">The queue that the consumer has been bound to.</param>
        /// <param name="exchange">The exchange that the consumer has been bound to.</param>
        [LoggerMessage(EventId = 314, Level = LogLevel.Information,
            Message = "Certificates: Cluster certificate consumer bound to {Queue} on {Exchange}")]
        private partial void LogClusterConsumerBound(string queue, string exchange);

        /// <summary>
        /// Logs that the cluster certificate message is not a valid envelope JSON.
        /// </summary>
        [LoggerMessage(EventId = 315, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate message is not a valid envelope JSON")]
        private partial void LogClusterInvalidEnvelope();

        /// <summary>
        /// Logs that the cluster certificate payload is missing or invalid.
        /// </summary>
        [LoggerMessage(EventId = 316, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate payload missing or invalid")]
        private partial void LogClusterInvalidPayload();

        /// <summary>
        /// Logs that the cluster certificate payload failed HMAC verification; not activating.
        /// </summary>
        [LoggerMessage(EventId = 317, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate payload failed HMAC verification; not activating")]
        private partial void LogClusterHmacVerificationFailed();

        /// <summary>
        /// Logs that the cluster certificate domain list does not match local ACME order domains; not activating.
        /// </summary>
        [LoggerMessage(EventId = 318, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate domain list does not match local ACME order domains; not activating")]
        private partial void LogClusterDomainMismatch();

        /// <summary>
        /// Logs that the cluster certificate PFX base64 is invalid.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        [LoggerMessage(EventId = 319, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate PFX base64 is invalid")]
        private partial void LogClusterInvalidPfxBase64(Exception ex);

        /// <summary>
        /// Logs that the cluster certificate is already expired; not activating.
        /// </summary>
        [LoggerMessage(EventId = 320, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate is already expired; not activating")]
        private partial void LogClusterCertificateExpired();

        /// <summary>
        /// Logs that the cluster certificate expiry metadata does not match PFX; not activating.
        /// </summary>
        [LoggerMessage(EventId = 321, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate expiry metadata does not match PFX; not activating")]
        private partial void LogClusterExpiryMetadataMismatch();

        /// <summary>
        /// Logs that the cluster certificate SHA-256 fingerprint mismatch; not activating.
        /// </summary>
        [LoggerMessage(EventId = 322, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate SHA-256 fingerprint mismatch; not activating")]
        private partial void LogClusterFingerprintMismatch();

        /// <summary>
        /// Logs that the cluster certificate IssuedAtUtc is too far in the future; not activating.
        /// </summary>
        [LoggerMessage(EventId = 323, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate IssuedAtUtc is too far in the future; not activating")]
        private partial void LogClusterIssuedAtTooFarInFuture();

        /// <summary>
        /// Logs that the cluster certificate IssuedAtUtc is too old; not activating.
        /// </summary>
        [LoggerMessage(EventId = 324, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate IssuedAtUtc is too old; not activating")]
        private partial void LogClusterIssuedAtTooOld();

        /// <summary>
        /// Logs that the cluster certificate epoch {Epoch} has been adopted.
        /// </summary>
        /// <param name="epoch">The epoch that has been adopted.</param>
        [LoggerMessage(EventId = 325, Level = LogLevel.Information,
            Message = "Certificates: Adopted cluster certificate epoch {Epoch}")]
        private partial void LogClusterCertificateAdopted(long epoch);

        /// <summary>
        /// Logs that the cluster certificate message handling failed.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        [LoggerMessage(EventId = 326, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate message handling failed")]
        private partial void LogClusterMessageHandlingFailed(Exception ex);

        #endregion
    }
}
