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
    public sealed partial class AuthoritativeDnsTxtPropagationProbe
    {
        /// <summary>
        /// Logs that authoritative DNS TXT quorum was satisfied for all challenge records.
        /// </summary>
        /// <param name="recordCount">Number of challenge records that reached quorum.</param>
        [LoggerMessage(EventId = 400, Level = LogLevel.Information,
            Message = "Certificates: DNS TXT quorum satisfied for {RecordCount} challenge record(s)")]
        private partial void LogDnsTxtQuorumSatisfied(int recordCount);
    }
}
