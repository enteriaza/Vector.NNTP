// <copyright file="ProcStatParser.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: parses Linux /proc/stat aggregate cpu jiffies.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Extracts aggregate CPU jiffies from a Linux <c>/proc/stat</c> text snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linux publishes one system-wide <c>cpu</c> line (not <c>cpu0</c>, <c>cpu1</c>, …) at the top of
    /// <c>/proc/stat</c>. Jiffies are unitless counters; callers compute busy percent from deltas between
    /// consecutive reads (see <see cref="LinuxHostCpuUsageSampler"/>).
    /// </para>
    /// <para>
    /// Busy jiffies sum user, nice, system, irq, softirq, and steal. Total jiffies add idle and iowait.
    /// Optional fields beyond the kernel minimum are treated as zero when absent. Guest and guest_nice
    /// columns are not included in the busy sum (consistent with common host-utilization formulas).
    /// </para>
    /// </remarks>
    public static class ProcStatParser
    {
        /// <summary>
        /// Attempts to parse busy and total jiffies from the aggregate <c>cpu</c> line in <c>/proc/stat</c> text.
        /// </summary>
        /// <param name="procStatContent">Full or partial <c>/proc/stat</c> content; lines may be LF-separated.</param>
        /// <param name="busyJiffies">
        /// When this method returns <see langword="true"/>, receives non-idle jiffies:
        /// user + nice + system + irq + softirq + steal.
        /// </param>
        /// <param name="totalJiffies">
        /// When this method returns <see langword="true"/>, receives busy jiffies plus idle and iowait jiffies.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when an aggregate line beginning with <c>cpu </c> was found, contained at least
        /// user/nice/system/idle columns, and produced a positive <paramref name="totalJiffies"/> value;
        /// otherwise <see langword="false"/> and both out parameters are zero.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="procStatContent"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>Expected column order on the aggregate line (1-based after the <c>cpu</c> label):</para>
        /// <list type="number">
        /// <item><description>user</description></item>
        /// <item><description>nice</description></item>
        /// <item><description>system</description></item>
        /// <item><description>idle</description></item>
        /// <item><description>iowait (optional)</description></item>
        /// <item><description>irq (optional)</description></item>
        /// <item><description>softirq (optional)</description></item>
        /// <item><description>steal (optional)</description></item>
        /// </list>
        /// <para>Per-CPU lines (<c>cpuN</c>) are ignored; only the first matching aggregate <c>cpu </c> line is used.</para>
        /// </remarks>
        public static bool TryParseAggregateCpuJiffies(string procStatContent, out ulong busyJiffies, out ulong totalJiffies)
        {
            busyJiffies = 0;
            totalJiffies = 0;
            ArgumentNullException.ThrowIfNull(procStatContent);

            foreach (string rawLine in procStatContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    return false;
                }

                ulong user = ParseUlong(parts[1]);
                ulong nice = ParseUlong(parts[2]);
                ulong system = ParseUlong(parts[3]);
                ulong idle = ParseUlong(parts[4]);
                ulong iowait = parts.Length > 5 ? ParseUlong(parts[5]) : 0;
                ulong irq = parts.Length > 6 ? ParseUlong(parts[6]) : 0;
                ulong softirq = parts.Length > 7 ? ParseUlong(parts[7]) : 0;
                ulong steal = parts.Length > 8 ? ParseUlong(parts[8]) : 0;

                busyJiffies = user + nice + system + irq + softirq + steal;
                ulong idleTotal = idle + iowait;
                totalJiffies = busyJiffies + idleTotal;
                return totalJiffies > 0;
            }

            return false;
        }

        /// <summary>
        /// Parses a decimal jiffies field, returning zero when the token is missing or not numeric.
        /// </summary>
        /// <param name="value">Single column token from a <c>/proc/stat</c> line.</param>
        /// <returns>Parsed unsigned value, or <c>0</c> when parsing fails.</returns>
        private static ulong ParseUlong(string value)
        {
            return ulong.TryParse(value, out ulong parsed) ? parsed : 0;
        }
    }
}
