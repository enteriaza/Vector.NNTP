// <copyright file="HistoryRedisTestAccessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using StackExchange.Redis;
using Vector.NNTP.Session.Redis.Coordination;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Routes StackExchange.Redis through a single test multiplexer for HistoryDB integration tests.
    /// </summary>
    internal sealed class HistoryRedisTestAccessor : IRedisConnectionAccessor
    {
        private readonly IConnectionMultiplexer _multiplexer;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRedisTestAccessor"/> class.
        /// </summary>
        /// <param name="multiplexer">Shared multiplexer.</param>
        public HistoryRedisTestAccessor(IConnectionMultiplexer multiplexer)
        {
            this._multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        }

        /// <inheritdoc />
        public IDatabase GetDatabase() => this._multiplexer.GetDatabase();

        /// <inheritdoc />
        public void SignalScaleUp()
        {
        }
    }
}
