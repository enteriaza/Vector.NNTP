// <copyright file="SpamdConnectionException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: TCP connect failures to spamd; distinct from post-connect protocol errors.
// SpamdConnectionException.cs -- Thrown only when <see cref="SpamdWireSession.ConnectAsync"/> cannot establish a TCP session.

using System.Runtime.Serialization;
using System.Security;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Indicates <see cref="SpamdWireSession.ConnectAsync"/> failed before a wire session was established.
    /// </summary>
    /// <remarks>
    /// <para><b>Contract:</b> Only <see cref="SpamdWireSession.ConnectAsync"/> throws this type. Post-connect send, receive, and parse failures
    /// use <see cref="SpamdProtocolException"/> instead so <see cref="SpamAssassin"/> connect-time failover does not mask protocol errors.</para>
    /// <para><b>Failover:</b> <see cref="SpamAssassin"/> catches this type when iterating configured hosts; callers above the client typically
    /// observe <see cref="SpamdProtocolException"/> because this type derives from it.</para>
    /// </remarks>
    [Serializable]
    public sealed class SpamdConnectionException : SpamdProtocolException
    {
        /// <summary>
        /// Initializes a new instance when no spamd hosts are configured or connect failed without a specific inner cause.
        /// </summary>
        /// <param name="message">Human-readable connect failure description.</param>
        public SpamdConnectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance wrapping the underlying socket, I/O, or cancellation failure from TCP connect.
        /// </summary>
        /// <param name="message">Human-readable connect failure description.</param>
        /// <param name="innerException">
        /// Underlying failure (typically <see cref="SocketException"/>, <see cref="IOException"/>, or
        /// <see cref="OperationCanceledException"/> from connect timeout or caller cancellation).
        /// </param>
        public SpamdConnectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Deserializes exception state produced by legacy binary formatters.
        /// </summary>
        /// <param name="info">Serialization payload.</param>
        /// <param name="context">Serialization stream context.</param>
        /// <exception cref="SerializationException">Thrown when required serialization entries are missing or invalid.</exception>
        /// <exception cref="SecurityException">Thrown when the caller lacks serialization permission.</exception>
        [Obsolete("This API supports obsolete formatter-based serialization. It should not be called from application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        private SpamdConnectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
