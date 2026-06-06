// <copyright file="NntpCommandGate.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: pre-dispatch security and authentication gating.

using Vector.NNTP.Sockets.Metrics;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Result of command gate evaluation.
    /// </summary>
    internal enum NntpGateResult : byte
    {
        /// <summary>Command may proceed to handler.</summary>
        Allow = 0,

        /// <summary>Reject with 480.</summary>
        AuthenticationRequired,

        /// <summary>Reject with 502.</summary>
        PermissionDenied,

        /// <summary>Reject with 483.</summary>
        TlsRequired,

        /// <summary>Reject with pre-encoded 502 (already authenticated).</summary>
        AlreadyAuthenticated,
    }

    /// <summary>
    /// Enforces unauthenticated allow-list, reader/transit profiles, and security ordering per Docs/nntp-security-ordering.md.
    /// </summary>
    internal static class NntpCommandGate
    {
        /// <summary>
        /// Evaluates whether <paramref name="verb"/> may run in the current session state.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="verb">Classified verb.</param>
        /// <returns>Gate result.</returns>
        internal static NntpGateResult Evaluate(NntpSession session, NntpKnownVerb verb)
        {
            return session.Connection.IsAuthenticated && verb == NntpKnownVerb.Authinfo
                ? NntpGateResult.AlreadyAuthenticated
                : verb == NntpKnownVerb.StartTls && session.State.IsCompressionActive
                ? NntpGateResult.PermissionDenied
                : verb == NntpKnownVerb.Authinfo && !session.IsAuthInfoPermitted
                ? NntpGateResult.TlsRequired
                : !session.Connection.IsAuthenticated
                ? EvaluateUnauthenticated(session, verb)
                : !IsAllowedForProfile(session, verb) ? NntpGateResult.PermissionDenied : NntpGateResult.Allow;
        }

        /// <summary>
        /// Writes the gate rejection response for <paramref name="result"/>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="result">Gate result.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteRejectionAsync(NntpSession session, NntpGateResult result, CancellationToken cancellationToken)
        {
            return result switch
            {
                NntpGateResult.AuthenticationRequired => session.Writer.WritePreencodedAsync(
                    NntpPreencodedResponses.AuthenticationRequired480, cancellationToken),
                NntpGateResult.PermissionDenied => session.Writer.WritePreencodedAsync(
                    NntpPreencodedResponses.PermissionDenied502, cancellationToken),
                NntpGateResult.TlsRequired => session.Writer.WritePreencodedAsync(
                    NntpPreencodedResponses.TlsRequired483, cancellationToken),
                NntpGateResult.AlreadyAuthenticated => session.Writer.WritePreencodedAsync(
                    NntpPreencodedResponses.AlreadyAuthenticated502, cancellationToken),
                NntpGateResult.Allow => throw new NotImplementedException(),
                _ => default,
            };
        }

        private static NntpGateResult EvaluateUnauthenticated(NntpSession session, NntpKnownVerb verb)
        {
            if (session.IsTrustedTransitPeer && IsStreamingVerb(verb))
            {
                string peerId = session.Connection.TransitPeerId!;
                if (verb == NntpKnownVerb.Check)
                {
                    NntpTransitPeerMetrics.RecordCheckWithoutAuth(peerId);
                }
                else if (verb == NntpKnownVerb.Ihave)
                {
                    NntpTransitPeerMetrics.RecordIhaveWithoutAuth(peerId);
                }
                else if (verb == NntpKnownVerb.Takethis)
                {
                    NntpTransitPeerMetrics.RecordTakethisWithoutAuth(peerId);
                }

                return NntpGateResult.Allow;
            }

            return IsStage1Allowed(verb) ? NntpGateResult.Allow : NntpGateResult.AuthenticationRequired;
        }

        private static bool IsStage1Allowed(NntpKnownVerb verb)
        {
            return verb is NntpKnownVerb.Capabilities
                or NntpKnownVerb.Mode
                or NntpKnownVerb.Quit
                or NntpKnownVerb.Date
                or NntpKnownVerb.Help
                or NntpKnownVerb.StartTls
                or NntpKnownVerb.Compress
                or NntpKnownVerb.Authinfo;
        }

        private static bool IsAllowedForProfile(NntpSession session, NntpKnownVerb verb)
        {
            return (session.Profile.AllowsReaderCommands && IsReaderVerb(verb)) || (session.Profile.AllowsStreamingCommands && IsStreamingVerb(verb)) || verb is NntpKnownVerb.Capabilities
                or NntpKnownVerb.Mode
                or NntpKnownVerb.Quit
                or NntpKnownVerb.Date
                or NntpKnownVerb.Help
                or NntpKnownVerb.StartTls
                or NntpKnownVerb.Compress
                or NntpKnownVerb.Authinfo;
        }

        private static bool IsReaderVerb(NntpKnownVerb verb)
        {
            return verb is NntpKnownVerb.Group
                or NntpKnownVerb.List
                or NntpKnownVerb.ListGroup
                or NntpKnownVerb.Article
                or NntpKnownVerb.Next
                or NntpKnownVerb.Last
                or NntpKnownVerb.Post
                or NntpKnownVerb.Over
                or NntpKnownVerb.Hdr
                or NntpKnownVerb.Newgroups
                or NntpKnownVerb.Newnews
                or NntpKnownVerb.Slave;
        }

        private static bool IsStreamingVerb(NntpKnownVerb verb)
        {
            return verb is NntpKnownVerb.Check or NntpKnownVerb.Ihave or NntpKnownVerb.Takethis;
        }
    }
}
