//-----------------------------------------------------------------------
// <copyright file="AuthoritativeDnsTxtPropagationProbe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Polls authoritative name servers until TXT challenge values reach a configurable quorum, using a minimal
    /// UDP/TCP wire client for TXT; NS delegation is discovered via recursive resolvers with optional per-zone NS caching.
    /// </summary>
    /// <remarks>
    /// <para><b>Logging:</b> <see cref="LoggerMessageAttribute"/> partial methods in
    /// <c>AuthoritativeDnsTxtPropagationProbe.Logging.cs</c>.</para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AuthoritativeDnsTxtPropagationProbe"/> class.
    /// </remarks>
    /// <param name="logger">Logger.</param>
    public sealed partial class AuthoritativeDnsTxtPropagationProbe(ILogger<AuthoritativeDnsTxtPropagationProbe> logger) : IDnsTxtPropagationProbe
    {
        private const int QuorumParallelism = 8;
        private readonly ILogger<AuthoritativeDnsTxtPropagationProbe> _logger = logger;
        private readonly ConcurrentDictionary<string, (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc)> _authoritativeNsByZone =
            new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public async Task WaitForTxtRecordsAsync(
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

            int minDelay = Math.Clamp(options.DnsPropagationDelaySeconds, 0, 300);
            if (minDelay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(minDelay), cancellationToken).ConfigureAwait(false);
            }

            TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Clamp(options.DnsTxtPollIntervalSeconds, 1, 60));
            TimeSpan budget = TimeSpan.FromSeconds(Math.Clamp(options.DnsTxtPollTimeoutSeconds, 5, 3600));
            double quorum = Math.Clamp(options.DnsAuthoritativeQuorumRatio, 0.5, 1.0);
            TimeSpan nsCacheTtl = TimeSpan.FromMinutes(Math.Clamp(options.DnsAuthoritativeNsCacheMinutes, 1, 60));

            DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;

            Dictionary<string, IReadOnlyList<IPAddress>> nsByRecord = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string recordName, _) in records)
            {
                if (!nsByRecord.ContainsKey(recordName))
                {
                    IReadOnlyList<IPAddress> ns = await DnsWireRecursiveResolver.ResolveAuthoritativeNameServerAddressesAsync(
                            recordName,
                            _authoritativeNsByZone,
                            nsCacheTtl,
                            cancellationToken)
                        .ConfigureAwait(false);
                    nsByRecord[recordName] = ns;
                }
            }

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool allOk = true;
                foreach ((string recordName, string expectedTxt) in records)
                {
                    IReadOnlyList<IPAddress> servers = nsByRecord[recordName];
                    if (!await QuorumShowsTxtAsync(recordName, expectedTxt, servers, quorum, cancellationToken).ConfigureAwait(false))
                    {
                        allOk = false;
                        break;
                    }
                }

                if (allOk)
                {
                    LogDnsTxtQuorumSatisfied(records.Count);
                    return;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"DNS TXT propagation did not reach quorum ({quorum:P0}) for all records within {budget}.");
        }

        private static async Task<bool> QuorumShowsTxtAsync(
            string recordName,
            string expectedTxt,
            IReadOnlyList<IPAddress> nameServers,
            double quorumRatio,
            CancellationToken cancellationToken)
        {
            if (nameServers.Count == 0)
            {
                List<string> txts = await DnsWireRecursiveResolver.QueryTxtRecursiveAsync(recordName, cancellationToken).ConfigureAwait(false);
                return TxtListContains(txts, expectedTxt);
            }

            int required = DnsAuthoritativeQuorum.RequiredMatchCount(nameServers.Count, quorumRatio);
            int ok = 0;
            int doneGate = 0;
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
                    List<string> txts = await AuthoritativeDnsWireClient.QueryTxtAsync(ip, recordName, ct).ConfigureAwait(false);
                    if (TxtListContains(txts, expectedTxt))
                    {
                        int newOk = Interlocked.Increment(ref ok);
                        if (newOk >= required)
                        {
                            Volatile.Write(ref doneGate, 1);
                        }
                    }
                }
                catch
                {
                    // treat as miss
                }
            }).ConfigureAwait(false);

            return ok >= required;
        }

        private static bool TxtListContains(IReadOnlyList<string> txts, string expectedTxt)
        {
            foreach (string? part in txts)
            {
                if (string.Equals(part, expectedTxt, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
