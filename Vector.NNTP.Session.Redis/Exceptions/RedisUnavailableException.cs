// <copyright file="RedisUnavailableException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Exceptions
{
    /// <summary>
    /// Thrown when no live Redis multiplexer is available in the coordination pool.
    /// </summary>
    /// <remarks>
    /// Session coordinators map this to <see cref="NntpSessionAdmissionResult.BackendFailure"/> (503) at the
    /// protocol layer.
    /// </remarks>
    public sealed class RedisUnavailableException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// </summary>
        public RedisUnavailableException()
            : base("Redis coordination pool has no live multiplexers.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public RedisUnavailableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class.
        /// </summary>
        /// <param name="message">Exception message.</param>
        /// <param name="innerException">Inner exception.</param>
        public RedisUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
