// <copyright file="NntpSpoolWriterHostedServiceTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Hosting;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Tests.HistoryDB.Fakes;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpSpoolWriterHostedService"/> scaling-loop resilience.
/// </summary>
[TestFixture]
public sealed class NntpSpoolWriterHostedServiceTests
{
    /// <summary>
    /// Verifies the scaling loop logs and continues after a policy fault on a scaling tick.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ExecuteAsync_ScalingFault_IsLoggedAndLoopContinues()
    {
        var queueOptions = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 4,
            MaxQueuedBytes = 4_194_304,
        });
        var queue = new NntpSpoolWriteQueue(queueOptions, new NntpSpoolMetrics());
        var serverOptions = Options.Create(new NntpServerOptions { SpoolDir = Path.GetTempPath() });
        var pump = new NntpSpoolWriterPump(
            queue,
            new ArticleSpoolPreprocessor(serverOptions),
            new ArticleSpoolPostprocessor(
                Options.Create(new PostFilterOptions()),
                serverOptions,
                new FakeSpamAssassin(),
                new SpamdScanArticleBuilder(),
                new NntpSpoolMetrics(),
                NullLogger<ArticleSpoolPostprocessor>.Instance),
            new FakeHistoryDatabase(),
            new NntpSpoolMetrics(),
            serverOptions,
            NullNntpNewsLog.Instance,
            NullLogger<NntpSpoolWriterPump>.Instance);
        var pool = new NntpSpoolWriterPool(
            queue,
            pump,
            new ThrowOnceScalingPolicy(),
            new NntpSpoolMetrics(),
            NullLogger<NntpSpoolWriterPool>.Instance);
        var logger = new ListLogger<NntpSpoolWriterHostedService>();
        var service = new NntpSpoolWriterHostedService(pool, logger);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(logger.Entries.Any(entry => entry.EventId == 202), Is.True);
        Assert.That(logger.Entries.Any(entry => entry.EventId == 200), Is.True);
    }

    /// <summary>
    /// Scaling policy that throws once from <see cref="ISpoolWriterScalingPolicy.ComputeDesiredWriters"/>.
    /// </summary>
    private sealed class ThrowOnceScalingPolicy : ISpoolWriterScalingPolicy
    {
        private int _calls;

        /// <inheritdoc />
        public int MinWriters => 1;

        /// <inheritdoc />
        public int MaxWriters => 4;

        /// <inheritdoc />
        public int ComputeDesiredWriters(long queueDepth, int queueCapacity)
        {
            if (Interlocked.Increment(ref this._calls) == 1)
            {
                throw new InvalidOperationException("Injected scaling policy fault.");
            }

            return 1;
        }
    }

    /// <summary>
    /// Captures structured log entries for assertions.
    /// </summary>
    /// <typeparam name="T">Logger category type.</typeparam>
    private sealed class ListLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Gets captured log entries.
        /// </summary>
        public List<(EventId EventId, LogLevel Level)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            this.Entries.Add((eventId, logLevel));
        }

        /// <summary>
        /// Null scope placeholder.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            /// <summary>
            /// Gets the singleton null scope instance.
            /// </summary>
            public static NullScope Instance { get; } = new();

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Minimal spamd client for hosted-service wiring tests.
    /// </summary>
    private sealed class FakeSpamAssassin : ISpamAssassin
    {
        /// <inheritdoc />
        public Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            _ = articleUtf8;
            _ = cancellationToken;
            return Task.FromResult(new SpamdCheckResult(
                false,
                score: 0,
                threshold: 5,
                symbols: [],
                reportText: null,
                rawResponseHeaders: new Dictionary<string, string>()));
        }
    }
}
