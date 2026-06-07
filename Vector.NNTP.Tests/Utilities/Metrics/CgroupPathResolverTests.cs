// <copyright file="CgroupPathResolverTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Utilities.Metrics;

/// <summary>
/// Tests for <see cref="CgroupPathResolver"/>.
/// </summary>
[TestFixture]
public sealed class CgroupPathResolverTests
{
    /// <summary>
    /// Verifies cgroup v2 path resolution when the directory exists.
    /// </summary>
    [Test]
    public void TryResolveFromCgroupFile_ResolvesV2Path()
    {
        string root = Path.Combine(Path.GetTempPath(), "cgroup-v2-" + Guid.NewGuid().ToString("N"));
        string slice = Path.Combine(root, "system.slice", "nntpd.service");
        Directory.CreateDirectory(slice);

        try
        {
            const string Cgroup = "0::/system.slice/nntpd.service\n";
            bool ok = CgroupPathResolver.TryResolveFromCgroupFile(Cgroup, root, out string dir, out bool isV2);
            Assert.That(ok, Is.True);
            Assert.That(isV2, Is.True);
            Assert.That(dir, Is.EqualTo(slice));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
