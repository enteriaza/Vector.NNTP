// <copyright file="ArticleSpoolPreprocessorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticleSpoolPreprocessor"/>.
/// </summary>
[TestFixture]
public sealed class ArticleSpoolPreprocessorTests
{
    /// <summary>
    /// Verifies a minimal valid article preprocesses successfully without <c>PathAppend</c>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PreprocessAsync_ValidArticle_ReturnsSuccess()
    {
        ArticleSpoolPreprocessor preprocessor = CreatePreprocessor();
        byte[] article = "Path: misc.test\r\nMessage-ID: <a@b>\r\n\r\n"u8.ToArray();

        ArticleSpoolPreprocessResult result = await preprocessor
            .PreprocessAsync("<a@b>", article)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ArticleBytes, Is.SameAs(article));
    }

    /// <summary>
    /// Verifies articles exceeding the internal header field count limit are rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PreprocessAsync_TooManyHeaderFields_ReturnsFailure()
    {
        ArticleSpoolPreprocessor preprocessor = CreatePreprocessor();
        byte[] article = BuildArticleWithHeaderFieldCount(257);

        ArticleSpoolPreprocessResult result = await preprocessor
            .PreprocessAsync("<many@test.local>", article)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("Too many header fields"));
    }

    /// <summary>
    /// Builds a preprocessor with empty <c>PathAppend</c>.
    /// </summary>
    /// <returns>Configured preprocessor instance.</returns>
    private static ArticleSpoolPreprocessor CreatePreprocessor()
    {
        var options = Options.Create(new NntpServerOptions { PathAppend = string.Empty });
        return new ArticleSpoolPreprocessor(options);
    }

    /// <summary>
    /// Builds an article with the requested number of simple header fields and a blank separator.
    /// </summary>
    /// <param name="headerFieldCount">Number of distinct header lines to emit.</param>
    /// <returns>Raw article bytes.</returns>
    private static byte[] BuildArticleWithHeaderFieldCount(int headerFieldCount)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < headerFieldCount; i++)
        {
            builder.Append("X-Field-").Append(i).Append(": value\r\n");
        }

        builder.Append("\r\n");
        return System.Text.Encoding.ASCII.GetBytes(builder.ToString());
    }
}
