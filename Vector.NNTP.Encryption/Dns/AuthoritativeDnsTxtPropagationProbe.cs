//-----------------------------------------------------------------------
// <copyright file="AuthoritativeDnsTxtPropagationProbe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using Vector.NNTP.Encryption.Configuration;
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
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AuthoritativeDnsTxtPropagationProbe"/> class.
    /// </remarks>
    /// <param name="logger">Logger.</param>
    public sealed partial class AuthoritativeDnsTxtPropagationProbe(ILogger<AuthoritativeDnsTxtPropagationProbe> logger) : IDnsTxtPropagationProbe
    {
        /// <summary>
        /// The maximum number of parallel name servers to check.
        /// </summary>
        private const int QuorumParallelism = 8;

        /// <summary>
        /// The cache of authoritative NS addresses by zone.
        /// </summary>
        private readonly ConcurrentDictionary<string, (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc)> _authoritativeNsByZone =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Waits for the TXT records to reach quorum.
        /// </summary>
        /// <param name="records">The records to check.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the TXT records reach quorum.</returns>
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
                            cancellationToken)
                        .ConfigureAwait(false);
                    nsByRecord[recordName] = ns;
                }
            }

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    return;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"DNS TXT propagation did not reach quorum ({quorum:P0}) for all records within {budget}.");
        }

        /// <summary>
        /// Checks if the quorum of name servers shows the expected TXT record.
        /// </summary>
        /// <param name="recordName">The name of the record to check.</param>
        /// <param name="expectedTxt">The expected TXT record bytes (ASCII challenge digest).</param>
        /// <param name="nameServers">The name servers to check.</param>
        /// <param name="quorumRatio">The quorum ratio.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="true"/> if the quorum of name servers shows the expected TXT record; otherwise <see langword="false"/>.</returns>
        private static async Task<bool> QuorumShowsTxtAsync(
            string recordName,
            byte[] expectedTxt,
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
                    if (await AuthoritativeDnsWireClient.QueryTxtContainsAsync(ip, recordName, expectedMemory, ct).ConfigureAwait(false))
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

        /// <summary>
        /// Checks if the list of TXT records contains the expected TXT record.
        /// </summary>
        /// <param name="txts">The list of TXT records.</param>
        /// <param name="expectedTxt">The expected TXT record bytes.</param>
        /// <returns><see langword="true"/> if the list of TXT records contains the expected TXT record; otherwise <see langword="false"/>.</returns>
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
