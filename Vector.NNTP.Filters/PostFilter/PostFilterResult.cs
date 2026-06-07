// <copyright file="PostFilterResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterResult.cs -- Outcome of evaluating a locally submitted article for the POST filter pipeline.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Outcome of evaluating a locally submitted article (maps to Perl <c>filter_post</c> return string / <c>DROP</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> COLD PATH — small value type returned to the NNTP layer; optional rewritten body is
    /// carried as a <see cref="ReadOnlyMemory{T}"/>.</para>
    /// </remarks>
    /// <param name="clientShouldSeeSuccess">When false, the host should send an NNTP 4xx style rejection with <see cref="ClientMessage"/>.</param>
    /// <param name="dropArticleAfterSuccessResponse">When true with <see cref="ClientShouldSeeSuccess"/>, the client may receive 240 while the article must not be stored.</param>
    /// <param name="code">Numeric postfilter-style code (0 on success path).</param>
    /// <param name="clientMessage">Human-readable rejection text when <see cref="ClientShouldSeeSuccess"/> is false.</param>
    /// <param name="modifiedArticleUtf8">Optional modified article bytes after header transforms.</param>
    public readonly struct PostFilterResult(
        bool clientShouldSeeSuccess,
        bool dropArticleAfterSuccessResponse,
        int code,
        string? clientMessage,
        ReadOnlyMemory<byte>? modifiedArticleUtf8)
    {

        /// <summary>
        /// When <see langword="false"/>, the host should emit an NNTP 4xx response using <see cref="ClientMessage"/> and <see cref="Code"/>.
        /// </summary>
        public bool ClientShouldSeeSuccess { get; } = clientShouldSeeSuccess;

        /// <summary>
        /// When <see langword="true"/> together with <see cref="ClientShouldSeeSuccess"/>, the client may receive 240 while the article must not be stored (Perl discard semantics).
        /// </summary>
        public bool DropArticleAfterSuccessResponse { get; } = dropArticleAfterSuccessResponse;

        /// <summary>Numeric postfilter-style rejection or audit code (0 on unconditional accept paths).</summary>
        public int Code { get; } = code;

        /// <summary>Human-readable rejection text for the NNTP layer when <see cref="ClientShouldSeeSuccess"/> is <see langword="false"/>.</summary>
        public string? ClientMessage { get; } = clientMessage;

        /// <summary>Rewritten article bytes after header transforms; <see langword="null"/> when the original buffer is returned unchanged.</summary>
        public ReadOnlyMemory<byte>? ModifiedArticleUtf8 { get; } = modifiedArticleUtf8;

        /// <summary>Builds an unconditional accept (no body rewrite).</summary>
        /// <returns>Accept result mapped to NNTP 240 with storage allowed.</returns>
        /// <remarks>Sets <see cref="ClientShouldSeeSuccess"/> to <see langword="true"/> and leaves <see cref="ModifiedArticleUtf8"/> null.</remarks>
        public static PostFilterResult Accept()
        {
            return new(clientShouldSeeSuccess: true, dropArticleAfterSuccessResponse: false, code: 0, clientMessage: null, modifiedArticleUtf8: null);
        }

        /// <summary>Builds an accept with optional modified article payload.</summary>
        /// <param name="modifiedArticleUtf8">Rewritten article bytes to store instead of the original POST buffer.</param>
        /// <returns>Accept result mapped to NNTP 240 with storage allowed.</returns>
        public static PostFilterResult AcceptWithBody(ReadOnlyMemory<byte> modifiedArticleUtf8)
        {
            return new(clientShouldSeeSuccess: true, dropArticleAfterSuccessResponse: false, code: 0, clientMessage: null, modifiedArticleUtf8: modifiedArticleUtf8);
        }

        /// <summary>Builds a hard reject visible to the NNTP client.</summary>
        /// <param name="code">Numeric postfilter audit code carried in <see cref="Code"/>.</param>
        /// <param name="clientMessage">Message for a 441-style rejection response.</param>
        /// <returns>Reject result with <see cref="ClientShouldSeeSuccess"/> <see langword="false"/>.</returns>
        public static PostFilterResult Reject(int code, string clientMessage)
        {
            return new(clientShouldSeeSuccess: false, dropArticleAfterSuccessResponse: false, code: code, clientMessage: clientMessage, modifiedArticleUtf8: null);
        }
    }
}

