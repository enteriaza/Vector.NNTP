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
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class with the default pool-empty message.
        /// </summary>
        public RedisUnavailableException()
            : base("Redis coordination pool has no live multiplexers.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class with a custom message.
        /// </summary>
        /// <param name="message">Human-readable explanation of why no live multiplexer is available.</param>
        public RedisUnavailableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisUnavailableException"/> class with a custom message and inner cause.
        /// </summary>
        /// <param name="message">Human-readable explanation of why no live multiplexer is available.</param>
        /// <param name="innerException">Underlying connection or pool failure.</param>
        public RedisUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
