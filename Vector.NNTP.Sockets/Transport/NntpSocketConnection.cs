// <copyright file="NntpSocketConnection.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bridges Socket to System.IO.Pipelines via NntpSocketTransport.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Creates <see cref="NntpSocketTransport"/> instances for accepted sockets.
    /// </summary>
    internal static class NntpSocketConnection
    {
        /// <summary>
        /// Creates a transport for a connected cleartext socket.
        /// </summary>
        /// <param name="socket">Connected socket.</param>
        /// <returns>Socket transport.</returns>
        internal static NntpSocketTransport CreateTransport(Socket socket) => new(socket);
    }
}
