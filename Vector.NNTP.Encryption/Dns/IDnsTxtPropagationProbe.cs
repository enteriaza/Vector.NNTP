// <copyright file="IDnsTxtPropagationProbe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: assembly-internal DNS TXT propagation contract for ACME DNS-01.

using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Waits until challenge TXT records are visible to enough authoritative name servers (quorum, not unanimity).
    /// </summary>
    internal interface IDnsTxtPropagationProbe
    {
        /// <summary>
        /// Polls until every <paramref name="records"/> entry satisfies the configured quorum ratio against authoritative NS
        /// (default 70% via <see cref="LetsEncryptOptions.DnsAuthoritativeQuorumRatio"/>; GeoDNS/anycast may disagree on a minority of NS),
        /// or the poll budget elapses.
        /// </summary>
        /// <param name="records">DNS names and expected TXT values.</param>
        /// <param name="options">Poll interval, timeout, quorum ratio.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when all records satisfy quorum.</returns>
        public Task WaitForTxtRecordsAsync(IReadOnlyList<(string RecordName, string ExpectedTxt)> records, LetsEncryptOptions options, CancellationToken cancellationToken);
    }
}
