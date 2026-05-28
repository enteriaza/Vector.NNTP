// <copyright file="NntpSessionAdmissionResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Outcome of a distributed session admission attempt.
    /// </summary>
    public enum NntpSessionAdmissionResult
    {
        /// <summary>
        /// Admission slot acquired (or limits disabled).
        /// </summary>
        Success,

        /// <summary>
        /// Cluster-wide <c>account_session_limit</c> reached.
        /// </summary>
        MaxSessionsExceeded,

        /// <summary>
        /// Distinct source IP cap (<c>account_srcip_limit</c>) reached.
        /// </summary>
        IpLimitExceeded,

        /// <summary>
        /// Redis or coordination backend unavailable or misconfigured.
        /// </summary>
        BackendFailure,

        /// <summary>
        /// Policy missing positive limits when admission is required.
        /// </summary>
        PolicyInvalid,
    }
}
