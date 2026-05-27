// <copyright file="NntpCommandVerbTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: verb classification unit tests (hot-path surrogate for BenchmarkDotNet in CI).

using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Unit tests for span-based NNTP verb classification.
    /// </summary>
    [TestFixture]
    public sealed class NntpCommandVerbTests
    {
        /// <summary>
        /// Verifies ARTICLE is classified correctly.
        /// </summary>
        [Test]
        public void Classify_Article_ReturnsArticleVerb()
        {
            NntpKnownVerb verb = NntpCommandVerb.Classify("ARTICLE 12345".AsSpan());
            Assert.That(verb, Is.EqualTo(NntpKnownVerb.Article));
        }

        /// <summary>
        /// Verifies CAPABILITIES is classified correctly.
        /// </summary>
        [Test]
        public void Classify_Capabilities_ReturnsCapabilitiesVerb()
        {
            NntpKnownVerb verb = NntpCommandVerb.Classify("CAPABILITIES".AsSpan());
            Assert.That(verb, Is.EqualTo(NntpKnownVerb.Capabilities));
        }
    }
}
