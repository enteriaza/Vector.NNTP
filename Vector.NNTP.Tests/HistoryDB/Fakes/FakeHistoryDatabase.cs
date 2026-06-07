// <copyright file="FakeHistoryDatabase.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Abstractions;

namespace Vector.NNTP.Tests.HistoryDB.Fakes
{
    /// <summary>
    /// In-memory history database for protocol golden tests.
    /// </summary>
    internal sealed class FakeHistoryDatabase : IHistoryDatabase
    {
        private readonly HashSet<string> _recorded = new(StringComparer.Ordinal);
        private bool _operational = true;

        /// <summary>
        /// Marks a message-id as already recorded (duplicate on CHECK and record).
        /// </summary>
        /// <param name="messageId">Message-id.</param>
        public void SeedDuplicate(string messageId) => this._recorded.Add(messageId);

        /// <summary>
        /// Sets operational state for tests.
        /// </summary>
        /// <param name="operational">Whether history is operational.</param>
        public void SetOperational(bool operational) => this._operational = operational;

        /// <inheritdoc />
        public ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._operational)
            {
                return ValueTask.FromResult(HistoryCheckResult.Unavailable);
            }

            return ValueTask.FromResult(
                this._recorded.Contains(messageId)
                    ? HistoryCheckResult.Duplicate
                    : HistoryCheckResult.Wanted);
        }

        /// <inheritdoc />
        public ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._operational)
            {
                return ValueTask.FromResult(HistoryRecordResult.Unavailable);
            }

            if (this._recorded.Contains(messageId))
            {
                return ValueTask.FromResult(HistoryRecordResult.Duplicate);
            }

            this._recorded.Add(messageId);
            return ValueTask.FromResult(HistoryRecordResult.Recorded);
        }

        /// <inheritdoc />
        public ValueTask<HistoryReleaseResult> TryReleaseAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._operational)
            {
                return ValueTask.FromResult(HistoryReleaseResult.Unavailable);
            }

            return this._recorded.Remove(messageId)
                ? ValueTask.FromResult(HistoryReleaseResult.Released)
                : ValueTask.FromResult(HistoryReleaseResult.NotFound);
        }
    }
}
