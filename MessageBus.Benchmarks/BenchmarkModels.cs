// <copyright file="BenchmarkModels.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text.Json;

namespace MessageBus.Benchmarks;

internal sealed class BenchmarkReport
{
    public string Topology { get; set; } = string.Empty;
    public string[] Hosts { get; set; } = [];
    public int Port { get; set; }
    public bool EnableSsl { get; set; }
    public int ChannelPoolSize { get; set; }
    public int RequestedChannelMax { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public double ConnectMilliseconds { get; set; }
    public List<ConcurrencyResult> Results { get; } = [];
    public string OverallStatus { get; private set; } = "Pending";
    public string OverallNotes { get; private set; } = string.Empty;

    public void EvaluatePassFail(double p99RpcSloMilliseconds)
    {
        ConcurrencyResult? worst = this.Results.OrderByDescending(static r => r.P99Milliseconds).FirstOrDefault();
        if (worst is null)
        {
            this.OverallStatus = "Fail";
            this.OverallNotes = "No concurrency results recorded.";
            return;
        }

        double slo = p99RpcSloMilliseconds;
        bool p99Ok = worst.P99Milliseconds <= slo;
        bool collapse = this.Results.Any(r => r.Concurrency >= 256 && r.P99Milliseconds > slo * 4);

        if (p99Ok && !collapse)
        {
            this.OverallStatus = "Pass";
            this.OverallNotes =
                $"Worst p99 CreateChannel {worst.P99Milliseconds:F1} ms at concurrency {worst.Concurrency} (SLO {slo} ms).";
        }
        else
        {
            this.OverallStatus = "Fail";
            this.OverallNotes =
                $"Worst p99 CreateChannel {worst.P99Milliseconds:F1} ms at concurrency {worst.Concurrency}; collapse detected={collapse}.";
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

internal sealed class ConcurrencyResult
{
    public int Concurrency { get; set; }
    public double TotalMilliseconds { get; set; }
    public double ChannelsPerSecond { get; set; }
    public double P50Milliseconds { get; set; }
    public double P99Milliseconds { get; set; }
    public double MaxMilliseconds { get; set; }
    public string Status => this.P99Milliseconds <= 500 ? "Pass" : "Fail";
}

internal sealed class BenchmarkFailureReport
{
    public string Topology { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTimeOffset FailedAtUtc { get; set; }
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
