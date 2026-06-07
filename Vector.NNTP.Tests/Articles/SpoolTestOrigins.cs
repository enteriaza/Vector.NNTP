// <copyright file="SpoolTestOrigins.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Shared peer-origin fixtures for transit spool tests.
/// </summary>
internal static class SpoolTestOrigins
{
    /// <summary>
    /// Gets a fixed UTC reception timestamp for deterministic spamd header tests.
    /// </summary>
    internal static DateTimeOffset SampleReceivedUtc { get; } =
        new(2026, 6, 7, 18, 42, 17, TimeSpan.Zero);

    /// <summary>
    /// Builds a transit storage origin for protocol and storage tests.
    /// </summary>
    /// <returns>Sample peer origin metadata.</returns>
    internal static NntpTransitArticleOrigin TransitOrigin()
    {
        return new NntpTransitArticleOrigin(
            IPAddress.Parse("203.0.113.10"),
            "border-3.ord.giganews.com",
            SampleReceivedUtc);
    }

    /// <summary>
    /// Builds a spool queue origin for writer and postprocessor tests.
    /// </summary>
    /// <returns>Sample spool origin metadata.</returns>
    internal static NntpSpoolArticleOrigin SpoolOrigin()
    {
        return new NntpSpoolArticleOrigin(
            IPAddress.Parse("203.0.113.10"),
            "border-3.ord.giganews.com",
            SampleReceivedUtc);
    }
}
