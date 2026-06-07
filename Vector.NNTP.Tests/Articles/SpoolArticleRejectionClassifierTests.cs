// <copyright file="SpoolArticleRejectionClassifierTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="SpoolArticleRejectionClassifier"/>.
/// </summary>
[TestFixture]
public sealed class SpoolArticleRejectionClassifierTests
{
    /// <summary>
    /// Verifies preprocess failures map to header syntax.
    /// </summary>
    [Test]
    public void ClassifyPreprocessFailure_ReturnsHeaderSyntax()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPreprocessFailure("Invalid header field at line 3: Path"),
            Is.EqualTo(SpoolArticleRejectionCategory.HeaderSyntax));
    }

    /// <summary>
    /// Verifies yEnc CRC failures map to the CRC bucket.
    /// </summary>
    [Test]
    public void ClassifyPostprocessFailure_Crc_ReturnsCrc()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(
                SpoolArticleRejectionClassifier.YEncCrcFailureReason),
            Is.EqualTo(SpoolArticleRejectionCategory.Crc));
    }

    /// <summary>
    /// Verifies Newsgroups limit failures map to crosspost.
    /// </summary>
    [Test]
    public void ClassifyPostprocessFailure_Crosspost_ReturnsCrosspost()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(
                "Newsgroups header lists 12 groups (limit 8)."),
            Is.EqualTo(SpoolArticleRejectionCategory.Crosspost));
    }

    /// <summary>
    /// Verifies header semantics failures map to header syntax.
    /// </summary>
    [Test]
    public void ClassifyPostprocessFailure_HeaderSemantics_ReturnsHeaderSyntax()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(
                "Required Message-ID header is missing."),
            Is.EqualTo(SpoolArticleRejectionCategory.HeaderSyntax));
    }

    /// <summary>
    /// Verifies spam failures map to other.
    /// </summary>
    [Test]
    public void ClassifyPostprocessFailure_Spam_ReturnsOther()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(
                "SpamAssassin classified article as spam (score 12.0/5.0)."),
            Is.EqualTo(SpoolArticleRejectionCategory.Other));
    }

    /// <summary>
    /// Verifies null or empty postprocess reasons map to other.
    /// </summary>
    [Test]
    public void ClassifyPostprocessFailure_NullOrEmpty_ReturnsOther()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(null),
            Is.EqualTo(SpoolArticleRejectionCategory.Other));
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(string.Empty),
            Is.EqualTo(SpoolArticleRejectionCategory.Other));
    }

    /// <summary>
    /// Verifies enqueue rejections map to other.
    /// </summary>
    [Test]
    public void ClassifyEnqueueFailure_ReturnsOther()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyEnqueueFailure("Queue full"),
            Is.EqualTo(SpoolArticleRejectionCategory.Other));
    }

    /// <summary>
    /// Verifies write failures map to other.
    /// </summary>
    [Test]
    public void ClassifyWriteFailure_ReturnsOther()
    {
        Assert.That(
            SpoolArticleRejectionClassifier.ClassifyWriteFailure(),
            Is.EqualTo(SpoolArticleRejectionCategory.Other));
    }
}
