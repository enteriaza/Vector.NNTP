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

        [LoggerMessage(EventId = 310, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate consumer could not be started; continuing without live fanout sync")]
        private partial void LogClusterConsumerStartFailed(Exception ex);

        [LoggerMessage(EventId = 311, Level = LogLevel.Information,
            Message = "Certificates: This node is not the ACME leader (queue {Queue} is held elsewhere)")]
        private partial void LogNotAcmeLeader(Exception ex, string queue);

        [LoggerMessage(EventId = 312, Level = LogLevel.Information,
            Message = "Certificates: Acquired ACME leader lock on queue {Queue}")]
        private partial void LogAcmeLeaderAcquired(string queue);

        [LoggerMessage(EventId = 313, Level = LogLevel.Information,
            Message = "Certificates: Published cluster certificate epoch {Epoch} to exchange {Exchange}")]
        private partial void LogClusterCertificatePublished(long epoch, string exchange);

        [LoggerMessage(EventId = 314, Level = LogLevel.Information,
            Message = "Certificates: Cluster certificate consumer bound to {Queue} on {Exchange}")]
        private partial void LogClusterConsumerBound(string queue, string exchange);

        [LoggerMessage(EventId = 315, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate message is not a valid envelope JSON")]
        private partial void LogClusterInvalidEnvelope();

        [LoggerMessage(EventId = 316, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate payload missing or invalid")]
        private partial void LogClusterInvalidPayload();

        [LoggerMessage(EventId = 317, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate payload failed HMAC verification; not activating")]
        private partial void LogClusterHmacVerificationFailed();

        [LoggerMessage(EventId = 318, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate domain list does not match local ACME order domains; not activating")]
        private partial void LogClusterDomainMismatch();

        [LoggerMessage(EventId = 319, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate PFX base64 is invalid")]
        private partial void LogClusterInvalidPfxBase64(Exception ex);

        [LoggerMessage(EventId = 320, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate is already expired; not activating")]
        private partial void LogClusterCertificateExpired();

        [LoggerMessage(EventId = 321, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate expiry metadata does not match PFX; not activating")]
        private partial void LogClusterExpiryMetadataMismatch();

        [LoggerMessage(EventId = 322, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate SHA-256 fingerprint mismatch; not activating")]
        private partial void LogClusterFingerprintMismatch();

        [LoggerMessage(EventId = 323, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate IssuedAtUtc is too far in the future; not activating")]
        private partial void LogClusterIssuedAtTooFarInFuture();

        [LoggerMessage(EventId = 324, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate IssuedAtUtc is too old; not activating")]
        private partial void LogClusterIssuedAtTooOld();

        [LoggerMessage(EventId = 325, Level = LogLevel.Information,
            Message = "Certificates: Adopted cluster certificate epoch {Epoch}")]
        private partial void LogClusterCertificateAdopted(long epoch);

        [LoggerMessage(EventId = 326, Level = LogLevel.Warning,
            Message = "Certificates: Cluster certificate message handling failed")]
        private partial void LogClusterMessageHandlingFailed(Exception ex);

        #endregion
    }
}
