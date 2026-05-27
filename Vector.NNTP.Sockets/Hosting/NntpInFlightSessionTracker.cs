// <copyright file="NntpInFlightSessionTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: tracks in-flight session tasks for graceful host shutdown.

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Counts active per-connection session tasks so the host can drain on shutdown.
    /// </summary>
    internal sealed class NntpInFlightSessionTracker
    {
        private int _inFlight;

        /// <summary>
        /// Gets the number of sessions currently running.
        /// </summary>
        internal int InFlight => Volatile.Read(ref this._inFlight);

        /// <summary>
        /// Registers the start of a session task.
        /// </summary>
        internal void Enter() => Interlocked.Increment(ref this._inFlight);

        /// <summary>
        /// Registers the end of a session task.
        /// </summary>
        internal void Leave() => Interlocked.Decrement(ref this._inFlight);

        /// <summary>
        /// Waits until all in-flight sessions complete or the token is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when drained or canceled.</returns>
        internal async Task DrainAsync(CancellationToken cancellationToken)
        {
            while (this.InFlight > 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
