// <copyright file="NntpSpoolThroughputLogTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Vector.NNTP.Articles.Hosting;
using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for single-line spool throughput minute log emission.
/// </summary>
[TestFixture]
public sealed class NntpSpoolThroughputLogTests
{
    /// <summary>
    /// Verifies zero-activity snapshots emit no log lines.
    /// </summary>
    [Test]
    public void EmitSnapshot_ZeroProcessed_EmitsNothing()
    {
        var logger = new CollectingLogger();
        var snapshot = new SpoolThroughputMinuteSnapshot(
            new SpoolThroughputFeedCounts(SpoolThroughputMinuteSnapshot.GlobalFeedLabel, 0, 0, 0, 0, 0),
            Array.Empty<SpoolThroughputFeedCounts>());

        NntpSpoolThroughputLog.EmitSnapshot(logger, snapshot);

        Assert.That(logger.Messages, Is.Empty);
    }

    /// <summary>
    /// Verifies active snapshots emit one global line and one line per feed.
    /// </summary>
    [Test]
    public void EmitSnapshot_WithActivity_EmitsGlobalAndPerFeedLines()
    {
        var logger = new CollectingLogger();
        var snapshot = new SpoolThroughputMinuteSnapshot(
            new SpoolThroughputFeedCounts(SpoolThroughputMinuteSnapshot.GlobalFeedLabel, 90, 5, 3, 2, 0),
            new[]
            {
                new SpoolThroughputFeedCounts("Giganews", 55, 2, 1, 1, 1),
                new SpoolThroughputFeedCounts("local", 35, 1, 1, 1, 2),
            });

        NntpSpoolThroughputLog.EmitSnapshot(logger, snapshot);

        Assert.That(logger.Messages, Has.Count.EqualTo(3));
        Assert.That(
            logger.Messages[0],
            Does.Contain("Spool throughput (60s): processed=100/min accepted=90 rejected=10 header=5 crc=3 crosspost=2 other=0"));
        Assert.That(
            logger.Messages[1],
            Does.Contain("Spool throughput (60s) feed=Giganews: processed=60/min accepted=55 rejected=5 header=2 crc=1 crosspost=1 other=1"));
        Assert.That(
            logger.Messages[2],
            Does.Contain("Spool throughput (60s) feed=local: processed=40/min accepted=35 rejected=5 header=1 crc=1 crosspost=1 other=2"));
    }

    /// <summary>
    /// Simple logger that records formatted Information messages.
    /// </summary>
    private sealed class CollectingLogger : ILogger
    {
        /// <summary>
        /// Gets formatted log messages emitted by this logger.
        /// </summary>
        internal List<string> Messages { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Information;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        /// <summary>
        /// Null scope returned from <see cref="ILogger.BeginScope{TState}"/>.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            /// <summary>
            /// Gets the shared null scope instance.
            /// </summary>
            internal static NullScope Instance { get; } = new();

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}
