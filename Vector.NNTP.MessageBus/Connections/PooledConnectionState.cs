// <copyright file="PooledConnectionState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// PooledConnectionState.cs -- Lifecycle enum for entries in ConnectionPool snapshots.
//
// Drives health aggregation (Connected vs faulted) and slot acquisition routing (skip stalled/blocked connections).

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Lifecycle state for a <see cref="PooledConnection"/> entry in <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Relationship to flow control:</b> <see cref="PooledConnection.IsBlocked"/> and
    /// <see cref="PooledConnection.IsStalled"/> are orthogonal to this enum — a connection may be <see cref="Connected"/>
    /// but blocked or stalled without transitioning to <see cref="Faulted"/>.</para>
    /// <para><b>Thread safety:</b> Written only from pool/supervisor paths that own the connection entry; read concurrently
    /// from slot acquisition and health aggregation.</para>
    /// </remarks>
    internal enum PooledConnectionState
    {
        /// <summary>
        /// TCP/AMQP handshake in progress; not yet eligible for publisher slots.
        /// </summary>
        Connecting = 0,
        /// <summary>
        /// Connection is established and may grant publisher slots when not blocked or stalled.
        /// </summary>
        Connected = 1,
        /// <summary>
        /// Host shutdown or pool drain in progress; no new publisher slots.
        /// </summary>
        Draining = 2,
        /// <summary>
        /// Unrecoverable session failure; entry is removed from the usable snapshot and replaced.
        /// </summary>
        Faulted = 3,
        /// <summary>
        /// Underlying <see cref="RabbitMQ.Client.IConnection"/> has been closed and disposed.
        /// </summary>
        Disposed = 4,
    }
}
