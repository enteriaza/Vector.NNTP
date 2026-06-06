// <copyright file="AuthMySqlFailureReason.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: classified backend failure reasons for structured authentication logs.

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Classified reason for a transient MySQL authentication backend failure.
    /// </summary>
    internal enum AuthMySqlFailureReason
    {
        /// <summary>
        /// Failure reason could not be determined from the exception type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// TCP or TLS connect to the database server timed out or was refused.
        /// </summary>
        ConnectTimeout = 1,

        /// <summary>
        /// An open connection timed out during command execution.
        /// </summary>
        QueryTimeout = 2,

        /// <summary>
        /// Connection pool contention prevented acquiring a connection in time.
        /// </summary>
        PoolPressure = 3,

        /// <summary>
        /// The operation was cancelled via <see cref="CancellationToken"/>.
        /// </summary>
        Cancelled = 4,
    }
}
