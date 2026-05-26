// <copyright file="Program.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using MessageBus.Benchmarks;
using MessageBus.Configuration;
using Microsoft.Extensions.Configuration;

string configPath = Path.GetFullPath(
    args.FirstOrDefault(static a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine("..", "NNRPD", "NNRPD.json"));

string topology = GetArg(args, "--topology") ?? "ha";
string outputPath = GetArg(args, "--output")
    ?? Path.GetFullPath(Path.Combine("..", "Docs", "message-bus-benchmark-results.json"));

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config not found: {configPath}");
    return 1;
}

IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false)
    .Build();

RabbitMQOptions options = new();
configuration.GetSection(RabbitMQOptions.SectionName).Bind(options);
ApplyTopology(options, topology);

Console.WriteLine($"Phase 4a CreateChannel benchmark");
Console.WriteLine($"  Config:    {configPath}");
Console.WriteLine($"  Topology:  {topology}");
Console.WriteLine($"  Hosts:     {string.Join(", ", options.Hosts)}");
Console.WriteLine($"  Port:      {options.Port} SSL={options.EnableSsl}");
Console.WriteLine($"  Output:    {outputPath}");
Console.WriteLine();

try
{
    CreateChannelBenchmarkRunner runner = new(options, topology);
    BenchmarkReport report = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

    string json = report.ToJson();
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);

    Console.WriteLine($"Overall: {report.OverallStatus} — {report.OverallNotes}");
    Console.WriteLine();
    Console.WriteLine("| Concurrency | p50 ms | p99 ms | max ms | ch/s | Status |");
    Console.WriteLine("|-------------|--------|--------|--------|------|--------|");
    foreach (ConcurrencyResult row in report.Results)
    {
        Console.WriteLine(
            $"| {row.Concurrency,11} | {row.P50Milliseconds,6:F1} | {row.P99Milliseconds,6:F1} | {row.MaxMilliseconds,6:F1} | {row.ChannelsPerSecond,4:F0} | {row.Status,-6} |");
    }

    Console.WriteLine();
    Console.WriteLine($"Wrote {outputPath}");
    return report.OverallStatus == "Pass" ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Benchmark failed: {ex.GetType().Name}: {ex.Message}");
    BenchmarkFailureReport failure = new()
    {
        Topology = topology,
        ConfigPath = configPath,
        Error = ex.ToString(),
        FailedAtUtc = DateTimeOffset.UtcNow,
    };

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllTextAsync(outputPath, failure.ToJson()).ConfigureAwait(false);
    return 1;
}

static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static void ApplyTopology(RabbitMQOptions options, string topology)
{
    switch (topology.ToLowerInvariant())
    {
        case "single":
            if (options.Hosts.Length > 0)
                options.Hosts = [options.Hosts[0]];
            break;
        case "tls":
            options.EnableSsl = true;
            if (options.Port == 5672)
                options.Port = 5671;
            break;
        case "ha":
        default:
            break;
    }
}
