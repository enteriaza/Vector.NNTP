// <copyright file="SpoolDirectoryUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="SpoolDirectoryUtilities"/>.
/// </summary>
[TestFixture]
public sealed class SpoolDirectoryUtilitiesTests
{
    /// <summary>
    /// Verifies empty <see cref="NntpServerOptions.SpoolDir"/> resolves under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    [Test]
    public void ResolveSpoolDirectory_Empty_UsesDefaultSubdirectory()
    {
        var options = new NntpServerOptions();
        string resolved = SpoolDirectoryUtilities.ResolveSpoolDirectory(options);
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, SpoolDirectoryUtilities.DefaultSpoolSubdirectory));
        Assert.That(resolved, Is.EqualTo(expected));
    }

    /// <summary>
    /// Verifies rooted <see cref="NntpServerOptions.SpoolDir"/> values are normalized with
    /// <see cref="Path.GetFullPath(string)"/>.
    /// </summary>
    [Test]
    public void ResolveSpoolDirectory_RootedPath_NormalizesParentSegments()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "spool-parent", "..", "spool-child");
        var options = new NntpServerOptions { SpoolDir = rooted };
        string resolved = SpoolDirectoryUtilities.ResolveSpoolDirectory(options);
        string expected = Path.GetFullPath(rooted);
        Assert.That(resolved, Is.EqualTo(expected));
        Assert.That(Path.IsPathRooted(resolved), Is.True);
    }

    /// <summary>
    /// Verifies relative <see cref="NntpServerOptions.SpoolDir"/> values resolve under
    /// <see cref="AppContext.BaseDirectory"/> and normalize parent segments.
    /// </summary>
    [Test]
    public void ResolveSpoolDirectory_RelativePath_NormalizesUnderBaseDirectory()
    {
        var options = new NntpServerOptions { SpoolDir = @".\Spool\..\SpoolResolved" };
        string resolved = SpoolDirectoryUtilities.ResolveSpoolDirectory(options);
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @".\Spool\..\SpoolResolved"));
        Assert.That(resolved, Is.EqualTo(expected));
        Assert.That(Path.IsPathRooted(resolved), Is.True);
    }

    /// <summary>
    /// Verifies digest fan-out paths use the configured fan-out prefix under <see cref="SpoolDirectoryUtilities.IncomingSubdirectory"/>.
    /// </summary>
    [Test]
    public void GetArticleFilePath_DigestHex_FansOutUnderIncoming()
    {
        const string messageId = "<fanout@test.local>";
        string digestHex = HistoryKeyEncoder.EncodeHexLower(messageId);
        string root = Path.Combine(Path.GetTempPath(), "spool-test");
        string path = SpoolDirectoryUtilities.GetArticleFilePath(root, digestHex);
        string expected = Path.Join(
            SpoolDirectoryUtilities.GetIncomingDirectory(root).AsSpan(),
            digestHex.AsSpan(0, SpoolDirectoryUtilities.FanoutLevelLength),
            digestHex.AsSpan(SpoolDirectoryUtilities.FanoutLevelLength, SpoolDirectoryUtilities.FanoutLevelLength),
            digestHex.AsSpan());
        Assert.That(path, Is.EqualTo(expected));
        Assert.That(SpoolDirectoryUtilities.FanoutPrefixHexLength, Is.EqualTo(SpoolDirectoryUtilities.FanoutLevelLength * SpoolDirectoryUtilities.FanoutLevelCount));
    }
}
