// <copyright file="CreateChannelBenchmarkRunner.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Text.Json;
using MessageBus.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace MessageBus.Benchmarks;

/// <summary>
/// Phase 4a CreateChannel contention benchmark on a single TCP connection.
/// </summary>
internal sealed class CreateChannelBenchmarkRunner
{
    private static readonly int[] ConcurrencySweep = [1, 8, 64, 256, 1024, 2048];

    private readonly RabbitMqConnectionFactory _factory;
    private readonly RabbitMQOptions _options;
    private readonly string _topologyLabel;

    /// <summary>Initializes a new instance of the <see cref="CreateChannelBenchmarkRunner"/> class.</summary>
    /// <param name="options">RabbitMQ options.</param>
    /// <param name="topologyLabel">Topology cell label for results.</param>
    public CreateChannelBenchmarkRunner(RabbitMQOptions options, string topologyLabel)
    {
        this._options = options;
        this._topologyLabel = topologyLabel;
        this._factory = new RabbitMqConnectionFactory(NullLogger<RabbitMqConnectionFactory>.Instance, new NullHostApplicationLifetime());
    }

    /// <summary>
    /// Runs the full concurrency sweep and returns structured results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Benchmark report.</returns>
    public async Task<BenchmarkReport> RunAsync(CancellationToken cancellationToken)
    {
        BenchmarkReport report = new()
        {
            Topology = this._topologyLabel,
            Hosts = this._options.Hosts,
            Port = this._options.Port,
            EnableSsl = this._options.EnableSsl,
            ChannelPoolSize = this._options.ChannelPoolSize,
            RequestedChannelMax = this._options.RequestedChannelMax,
            StartedAtUtc = DateTimeOffset.UtcNow,
            MachineName = Environment.MachineName,
        };

        Stopwatch connectWatch = Stopwatch.StartNew();
        await using IConnection connection = await this._factory.CreateConnectionAsync(this._options, cancellationToken)
            .ConfigureAwait(false);
        connectWatch.Stop();
        report.ConnectMilliseconds = connectWatch.Elapsed.TotalMilliseconds;

        foreach (int concurrency in ConcurrencySweep)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConcurrencyResult row = await this.RunConcurrencyAsync(connection, concurrency, cancellationToken)
                .ConfigureAwait(false);
            report.Results.Add(row);
        }

        report.CompletedAtUtc = DateTimeOffset.UtcNow;
        report.EvaluatePassFail(p99RpcSloMilliseconds: 500);
        return report;
    }

    private async Task<ConcurrencyResult> RunConcurrencyAsync(
        IConnection connection,
        int concurrency,
        CancellationToken cancellationToken)
    {
        long[] latenciesMs = new long[concurrency];
        Stopwatch totalWatch = Stopwatch.StartNew();

        Task[] tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            int index = i;
            tasks[index] = Task.Run(async () =>
            {
                Stopwatch sw = Stopwatch.StartNew();
                CreateChannelOptions channelOptions = new(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true);
                IChannel channel = await connection.CreateChannelAsync(channelOptions, cancellationToken)
                    .ConfigureAwait(false);
                sw.Stop();
                latenciesMs[index] = sw.ElapsedMilliseconds;
                await channel.CloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await channel.DisposeAsync().ConfigureAwait(false);
            }, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        totalWatch.Stop();

        Array.Sort(latenciesMs);
        return new ConcurrencyResult
        {
            Concurrency = concurrency,
            TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds,
            ChannelsPerSecond = concurrency / Math.Max(totalWatch.Elapsed.TotalSeconds, 0.001),
            P50Milliseconds = Percentile(latenciesMs, 0.50),
            P99Milliseconds = Percentile(latenciesMs, 0.99),
            MaxMilliseconds = latenciesMs[^1],
        };
    }

    private static double Percentile(long[] sorted, double percentile)
    {
        if (sorted.Length == 0)
            return 0;

        double index = percentile * (sorted.Length - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return sorted[lower];

        double weight = index - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * weight);
    }

    /// <summary>Null host lifetime for benchmark-only factory construction.</summary>
    private sealed class NullHostApplicationLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
        }
    }
}
