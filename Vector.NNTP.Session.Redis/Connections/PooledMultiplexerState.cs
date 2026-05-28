// <copyright file="PooledMultiplexerState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Lifecycle state for an entry in <see cref="RedisMultiplexerPool"/>.
    /// </summary>
    public enum PooledMultiplexerState
    {
        /// <summary>Multiplexer is connected and eligible for use.</summary>
        Connected = 0,

        /// <summary>Multiplexer failed and was removed from the active snapshot.</summary>
        Faulted = 1,
    }
}
