// <copyright file="AuthoritativeDnsTxtPropagationProbe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: authoritative DNS TXT propagation polling for ACME DNS-01 challenges.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Telemetry;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Polls authoritative name servers until TXT challenge values reach a configurable quorum, using a minimal
    /// UDP/TCP wire client for TXT; NS delegation is discovered via recursive resolvers with optional per-zone NS caching.
    /// </summary>
    /// <remarks>
    /// <para><b>Logging:</b> <see cref="LoggerMessageAttribute"/> partial methods in
    /// <c>AuthoritativeDnsTxtPropagationProbe.Logging.cs</c>.</para>
    /// <para><b>Construction:</b> Primary constructor receives the logger used for propagation diagnostics and optional
    /// <see cref="EncryptionMetrics"/> for propagation duration recording.</para>
    /// </remarks>
    /// <param name="logger">Logger for propagation polling diagnostics.</param>
    /// <param name="metrics">Optional metrics recorder for DNS propagation duration.</param>
    internal sealed partial class AuthoritativeDnsTxtPropagationProbe(
        ILogger<AuthoritativeDnsTxtPropagationProbe> logger,
        EncryptionMetrics? metrics = null) : IDnsTxtPropagationProbe
    {
        /// <summary>
        /// Maximum concurrent authoritative NS queries per TXT record during quorum polling.
        /// </summary>
        private const int QuorumParallelism = 8;

        /// <summary>
        /// Per-zone cache of resolved authoritative NS addresses with TTL expiry to avoid repeated delegation walks.
        /// </summary>
        /// <remarks>Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>; entries expire per options TTL.</remarks>
        private readonly ConcurrentDictionary<string, (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc)> _authoritativeNsByZone =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Polls authoritative name servers until every challenge TXT satisfies the configured quorum ratio or the budget elapses.
        /// </summary>
        /// <param name="records">DNS names and expected ACME DNS-01 TXT digests.</param>
        /// <param name="options">Poll interval, timeout, quorum ratio, and NS cache TTL.</param>
        /// <param name="cancellationToken">Host shutdown token observed between poll iterations.</param>
        /// <returns>A task that completes when all entries satisfy quorum.</returns>
        /// <exception cref="TimeoutException">Thrown when the poll budget elapses before quorum is reached.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is signalled.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="records"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
        async Task IDnsTxtPropagationProbe.WaitForTxtRecordsAsync(
            IReadOnlyList<(string RecordName, string ExpectedTxt)> records,
            LetsEncryptOptions options,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(options);
            if (records.Count == 0)
            {
                return;
            }

            using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                "encryption.dns.propagation",
                ActivityKind.Client);
            _ = activity?.SetTag("encryption.dns.record_count", records.Count);

            int minDelay = Math.Clamp(options.DnsPropagationDelaySeconds, 0, 300);
            if (minDelay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(minDelay), cancellationToken).ConfigureAwait(false);
            }

            TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Clamp(options.DnsTxtPollIntervalSeconds, 1, 60));
            TimeSpan budget = TimeSpan.FromSeconds(Math.Clamp(options.DnsTxtPollTimeoutSeconds, 5, 3600));
            double quorum = Math.Clamp(options.DnsAuthoritativeQuorumRatio, 0.5, 1.0);
            TimeSpan nsCacheTtl = TimeSpan.FromMinutes(Math.Clamp(options.DnsAuthoritativeNsCacheMinutes, 1, 60));
            int budgetSeconds = (int)budget.TotalSeconds;

            DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
            DateTimeOffset propagationStart = DateTimeOffset.UtcNow;
            int pollIteration = 0;

            Dictionary<string, IReadOnlyList<IPAddress>> nsByRecord = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, byte[]> expectedBytesByRecord = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string recordName, string expectedTxt) in records)
            {
                expectedBytesByRecord[recordName] = EncodingUtilities.AsciiToBytes(expectedTxt);
                if (!nsByRecord.ContainsKey(recordName))
                {
                    IReadOnlyList<IPAddress> ns = await DnsWireRecursiveResolver.ResolveAuthoritativeNameServerAddressesAsync(
                            recordName,
                            _authoritativeNsByZone,
                            nsCacheTtl,
                            logger,
                            cancellationToken)
                        .ConfigureAwait(false);
                    nsByRecord[recordName] = ns;
                }
            }

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pollIteration++;
                int remainingSeconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds));
                LogDnsTxtPollIteration(pollIteration, records.Count, remainingSeconds);

                bool allOk = true;
                foreach ((string recordName, _) in records)
                {
                    IReadOnlyList<IPAddress> servers = nsByRecord[recordName];
                    byte[] expectedBytes = expectedBytesByRecord[recordName];
                    if (!await QuorumShowsTxtAsync(recordName, expectedBytes, servers, quorum, cancellationToken).ConfigureAwait(false))
                    {
                        allOk = false;
                        break;
                    }
                }

                if (allOk)
                {
                    LogDnsTxtQuorumSatisfied(records.Count);
                    metrics?.RecordDnsPropagationDuration((DateTimeOffset.UtcNow - propagationStart).TotalMilliseconds);
                    return;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }

            LogDnsTxtPropagationTimeout(quorum, budgetSeconds);
            throw new TimeoutException(
                $"DNS TXT propagation did not reach quorum ({quorum:P0}) for all records within {budget}.");
        }

        /// <summary>
        /// Checks if the quorum of name servers shows the expected TXT record.
        /// </summary>
        /// <param name="recordName">Challenge FQDN being polled.</param>
        /// <param name="expectedTxt">Expected ASCII challenge digest bytes.</param>
        /// <param name="nameServers">Authoritative NS addresses for <paramref name="recordName"/>; empty list triggers recursive fallback.</param>
        /// <param name="quorumRatio">Minimum fraction of NS that must return a matching TXT (for example 0.7).</param>
        /// <param name="cancellationToken">Cancellation token for parallel NS queries.</param>
        /// <returns>
        /// <see langword="true"/> when at least <see cref="DnsAuthoritativeQuorum.RequiredMatchCount"/> servers return the
        /// expected TXT; otherwise <see langword="false"/>.
        /// </returns>
        private async Task<bool> QuorumShowsTxtAsync(
            string recordName,
            byte[] expectedTxt,
            IReadOnlyList<IPAddress> nameServers,
            double quorumRatio,
            CancellationToken cancellationToken)
        {
            if (nameServers.Count == 0)
            {
                List<string> txts = await DnsWireRecursiveResolver.QueryTxtRecursiveAsync(recordName, logger, cancellationToken)
                    .ConfigureAwait(false);
                return TxtListContains(txts, expectedTxt);
            }

            int required = DnsAuthoritativeQuorum.RequiredMatchCount(nameServers.Count, quorumRatio);
            int ok = 0;
            int doneGate = 0;
            ReadOnlyMemory<byte> expectedMemory = expectedTxt;
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = QuorumParallelism,
                CancellationToken = cancellationToken,
            };

            await Parallel.ForEachAsync(nameServers, parallelOptions, async (ip, ct) =>
            {
                if (Volatile.Read(ref doneGate) != 0)
                {
                    return;
                }

                try
                {
                    if (await AuthoritativeDnsWireClient.QueryTxtContainsAsync(ip, recordName, expectedMemory, logger, ct)
                            .ConfigureAwait(false))
                    {
                        int newOk = Interlocked.Increment(ref ok);
                        if (newOk >= required)
                        {
                            Volatile.Write(ref doneGate, 1);
                        }
                    }
                    else
                    {
                        LogDnsTxtNameserverMiss(recordName, ip.ToString(), "no matching TXT");
                    }
                }
                catch (Exception ex)
                {
                    LogDnsTxtNameserverMiss(recordName, ip.ToString(), ex.GetType().Name);
                }
            }).ConfigureAwait(false);

            return ok >= required;
        }

        /// <summary>
        /// Checks if the list of TXT records contains the expected TXT record.
        /// </summary>
        /// <param name="txts">TXT strings returned by a resolver or wire client.</param>
        /// <param name="expectedTxt">Expected ASCII challenge digest bytes.</param>
        /// <returns>
        /// <see langword="true"/> when any same-length printable ASCII TXT equals <paramref name="expectedTxt"/> byte-for-byte.
        /// </returns>
        private static bool TxtListContains(IReadOnlyList<string> txts, ReadOnlySpan<byte> expectedTxt)
        {
            foreach (string? part in txts)
            {
                if (part is null || part.Length != expectedTxt.Length)
                {
                    continue;
                }

                if (!EncodingUtilities.IsAscii(part.AsSpan()))
                {
                    continue;
                }

                ReadOnlySpan<char> chars = part.AsSpan();
                bool match = true;
                for (int i = 0; i < expectedTxt.Length; i++)
                {
                    if ((byte)chars[i] != expectedTxt[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
