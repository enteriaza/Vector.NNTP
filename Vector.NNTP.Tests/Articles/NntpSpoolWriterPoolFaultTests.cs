// <copyright file="NntpSpoolWriterPoolFaultTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Tests for <see cref="NntpSpoolWriterPool"/> worker fault logging and scale metrics.
/// </summary>
[TestFixture]
public sealed class NntpSpoolWriterPoolFaultTests
{
    /// <summary>
    /// Verifies shutdown await logs a faulted worker task through EventId 201.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task StopAsync_FaultedWorker_LogsWorkerTaskFaulted()
    {
        NntpSpoolWriterPool pool = CreatePool(out _);
        var logger = new ListLogger<NntpSpoolWriterPool>();
        ReplacePoolLogger(pool, logger);

        await pool.StartAsync(CancellationToken.None).ConfigureAwait(false);
        InjectFaultedWorker(pool);
        await pool.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(logger.Entries.Any(entry => entry.EventId == 201), Is.True);
    }

    /// <summary>
    /// Verifies scale-up records the writer scale counter with direction <c>up</c>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task AdjustWriterCountAsync_ScaleUp_RecordsWriterScaleCounter()
    {
        NntpSpoolWriterPool pool = CreatePool(out NntpSpoolMetrics metrics);
        var scaleEvents = new List<string>();
        using System.Diagnostics.Metrics.MeterListener listener = CreateScaleListener(scaleEvents);

        await pool.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await pool.AdjustWriterCountAsync(2, CancellationToken.None).ConfigureAwait(false);

        Assert.That(scaleEvents, Does.Contain("up"));
    }

    /// <summary>
    /// Builds a writer pool for fault and scaling tests.
    /// </summary>
    /// <param name="metrics">Created metrics instance wired into the pool.</param>
    /// <returns>Configured pool instance.</returns>
    private static NntpSpoolWriterPool CreatePool(out NntpSpoolMetrics metrics)
    {
        metrics = new NntpSpoolMetrics();
        var queueOptions = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 4,
            MaxQueuedBytes = 4_194_304,
            SpoolDir = Path.GetTempPath(),
        });
        var queue = new NntpSpoolWriteQueue(queueOptions, metrics);
        var serverOptions = Options.Create(new NntpServerOptions { SpoolDir = Path.GetTempPath() });
        var pump = new NntpSpoolWriterPump(
            queue,
            new ArticleSpoolPreprocessor(serverOptions),
            new ArticleSpoolPostprocessor(
                Options.Create(new PostFilterOptions()),
                serverOptions,
                new FakeSpamAssassin(),
                new SpamdScanArticleBuilder(),
                metrics,
                NullLogger<ArticleSpoolPostprocessor>.Instance),
            new FakeHistoryDatabase(),
            metrics,
            serverOptions,
            NullNntpNewsLog.Instance,
            NullLogger<NntpSpoolWriterPump>.Instance);

        return new NntpSpoolWriterPool(
            queue,
            pump,
            new ProcessorQueueSpoolWriterScalingPolicy(),
            metrics,
            NullLogger<NntpSpoolWriterPool>.Instance);
    }

    /// <summary>
    /// Replaces the pool logger field with a test list logger.
    /// </summary>
    /// <param name="pool">Pool under test.</param>
    /// <param name="logger">Replacement logger.</param>
    private static void ReplacePoolLogger(NntpSpoolWriterPool pool, ListLogger<NntpSpoolWriterPool> logger)
    {
        FieldInfo? loggerField = typeof(NntpSpoolWriterPool).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(loggerField, Is.Not.Null);
        loggerField!.SetValue(pool, logger);
    }

    /// <summary>
    /// Replaces the active worker list with a single faulted worker task.
    /// </summary>
    /// <param name="pool">Pool under test.</param>
    private static void InjectFaultedWorker(NntpSpoolWriterPool pool)
    {
        FieldInfo? workersField = typeof(NntpSpoolWriterPool).GetField("_workers", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(workersField, Is.Not.Null);
        object workers = workersField!.GetValue(pool)!;
        Type workerType = typeof(NntpSpoolWriterPool).GetNestedType("Worker", BindingFlags.NonPublic)!;
        object faultedWorker = Activator.CreateInstance(
            workerType,
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None),
            Task.FromException(new InvalidOperationException("Injected worker fault.")))!;
        MethodInfo? clearMethod = workers.GetType().GetMethod("Clear");
        MethodInfo? addMethod = workers.GetType().GetMethod("Add");
        Assert.That(clearMethod, Is.Not.Null);
        Assert.That(addMethod, Is.Not.Null);
        clearMethod!.Invoke(workers, null);
        addMethod!.Invoke(workers, [faultedWorker]);
    }

    /// <summary>
    /// Creates a listener that captures writer scale counter direction tags.
    /// </summary>
    /// <param name="directions">Captured scale directions.</param>
    /// <returns>A started listener; dispose after exercising metrics.</returns>
    private static System.Diagnostics.Metrics.MeterListener CreateScaleListener(List<string> directions)
    {
        var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "nntp.spool.writers.scale_total")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> entry in tags)
            {
                if (entry.Key == "direction" && entry.Value is string direction)
                {
                    directions.Add(direction);
                }
            }
        });

        listener.Start();
        return listener;
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
    /// Minimal spamd client for pool wiring tests.
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
