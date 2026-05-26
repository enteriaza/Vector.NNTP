// <copyright file="LengthLimitedReadStreamTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="LengthLimitedReadStream"/> byte-limit enforcement and disposal guards.
/// </summary>
[TestFixture]
public sealed class LengthLimitedReadStreamTests
{
    /// <summary>
    /// Verifies reads up to the limit succeed and the next read throws.
    /// </summary>
    [Test]
    public void Read_EnforcesExclusiveUpperBound()
    {
        byte[] payload = new byte[] { 1, 2, 3, 4, 5 };
        using MemoryStream inner = new(payload, writable: false);
        using LengthLimitedReadStream limited = new(inner, maxBytes: 3, operation: "GET /test");

        byte[] buffer = new byte[8];

        Assert.That(limited.Read(buffer), Is.EqualTo(3));
        Assert.That(buffer.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

        Assert.Throws<InvalidOperationException>(() => limited.Read(buffer));
    }

    /// <summary>
    /// Verifies disposal prevents further reads.
    /// </summary>
    [Test]
    public void Read_AfterDispose_ThrowsObjectDisposedException()
    {
        using MemoryStream inner = new(new byte[] { 1, 2, 3 }, writable: false);
        LengthLimitedReadStream limited = new(inner, maxBytes: 10, operation: "GET /test");
        limited.Dispose();

        byte[] buffer = new byte[4];
        Assert.Throws<ObjectDisposedException>(() => limited.Read(buffer));
    }
}
