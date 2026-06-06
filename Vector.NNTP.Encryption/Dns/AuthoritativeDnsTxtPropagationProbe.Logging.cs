// <copyright file="AuthoritativeDnsTxtPropagationProbe.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AuthoritativeDnsTxtPropagationProbe.Logging.cs -- Source-generated [LoggerMessage] partial methods.

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for
    /// <see cref="AuthoritativeDnsTxtPropagationProbe"/>.
    /// </summary>
    internal sealed partial class AuthoritativeDnsTxtPropagationProbe
    {
        /// <summary>
        /// Logs that authoritative DNS TXT quorum was satisfied for all challenge records.
        /// </summary>
        /// <param name="recordCount">Number of challenge records that reached quorum.</param>
        [LoggerMessage(EventId = 400, Level = LogLevel.Information,
            Message = "Certificates: DNS TXT quorum satisfied for {RecordCount} challenge record(s)")]
        private partial void LogDnsTxtQuorumSatisfied(int recordCount);

        /// <summary>
        /// Logs a DNS TXT propagation poll iteration while quorum is not yet satisfied.
        /// </summary>
        /// <param name="iteration">Poll iteration number (1-based).</param>
        /// <param name="recordCount">Number of challenge records being polled.</param>
        /// <param name="remainingSeconds">Seconds remaining before the poll budget elapses.</param>
        [LoggerMessage(EventId = 401, Level = LogLevel.Debug,
            Message = "Certificates: DNS TXT propagation poll iteration {Iteration} for {RecordCount} record(s); {RemainingSeconds}s remaining")]
        private partial void LogDnsTxtPollIteration(int iteration, int recordCount, int remainingSeconds);

        /// <summary>
        /// Logs that DNS TXT propagation did not reach quorum before the poll budget elapsed.
        /// </summary>
        /// <param name="quorumRatio">Configured quorum ratio.</param>
        /// <param name="budgetSeconds">Total poll budget in seconds.</param>
        [LoggerMessage(EventId = 402, Level = LogLevel.Warning,
            Message = "Certificates: DNS TXT propagation timed out before reaching {QuorumRatio:P0} quorum within {BudgetSeconds}s")]
        private partial void LogDnsTxtPropagationTimeout(double quorumRatio, int budgetSeconds);

        /// <summary>
        /// Logs that an authoritative nameserver did not yet show the expected TXT value.
        /// </summary>
        /// <param name="recordName">Challenge record name queried.</param>
        /// <param name="nameserver">Authoritative nameserver address.</param>
        /// <param name="reason">Miss reason (for example timeout or exception type name).</param>
        [LoggerMessage(EventId = 403, Level = LogLevel.Debug,
            Message = "Certificates: DNS TXT miss on {Nameserver} for {RecordName} ({Reason})")]
        private partial void LogDnsTxtNameserverMiss(string recordName, string nameserver, string reason);
    }
}
