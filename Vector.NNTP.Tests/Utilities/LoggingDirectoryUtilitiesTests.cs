// <copyright file="LoggingDirectoryUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Configuration;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/>.
/// </summary>
[TestFixture]
public sealed class LoggingDirectoryUtilitiesTests
{
    /// <summary>
    /// Verifies missing <c>Logging:LogDir</c> returns the default under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    [Test]
    public void ResolveLogDirectory_MissingKey_ReturnsDefaultUnderBaseDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        string resolved = LoggingDirectoryUtilities.ResolveLogDirectory(configuration);

        Assert.That(resolved, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, LoggingDirectoryUtilities.DefaultLogSubdirectory)));
    }

    /// <summary>
    /// Verifies whitespace <c>Logging:LogDir</c> returns the default under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    [Test]
    public void ResolveLogDirectory_EmptyValue_ReturnsDefaultUnderBaseDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingDirectoryUtilities.SectionName}:{LoggingDirectoryUtilities.LogDirKey}"] = "   ",
            })
            .Build();

        string resolved = LoggingDirectoryUtilities.ResolveLogDirectory(configuration);

        Assert.That(resolved, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, LoggingDirectoryUtilities.DefaultLogSubdirectory)));
    }

    /// <summary>
    /// Verifies absolute <c>Logging:LogDir</c> values are returned unchanged.
    /// </summary>
    [Test]
    public void ResolveLogDirectory_AbsolutePath_ReturnsTrimmedPath()
    {
        string absolutePath = Path.Combine(Path.GetTempPath(), "vector-nntp-logs");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingDirectoryUtilities.SectionName}:{LoggingDirectoryUtilities.LogDirKey}"] = $"  {absolutePath}  ",
            })
            .Build();

        string resolved = LoggingDirectoryUtilities.ResolveLogDirectory(configuration);

        Assert.That(resolved, Is.EqualTo(absolutePath));
    }

    /// <summary>
    /// Verifies relative <c>Logging:LogDir</c> values resolve under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    [Test]
    public void ResolveLogDirectory_RelativePath_ResolvesUnderBaseDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingDirectoryUtilities.SectionName}:{LoggingDirectoryUtilities.LogDirKey}"] = "custom/logs",
            })
            .Build();

        string resolved = LoggingDirectoryUtilities.ResolveLogDirectory(configuration);
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "custom/logs"));

        Assert.That(resolved, Is.EqualTo(expected));
    }
}
