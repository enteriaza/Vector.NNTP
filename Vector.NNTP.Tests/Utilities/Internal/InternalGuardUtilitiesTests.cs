// <copyright file="InternalGuardUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Internal;

namespace Vector.NNTP.Tests.Utilities.Internal;

/// <summary>
/// Tests for <see cref="GuardUtilities"/> disposed-state semantics.
/// </summary>
[TestFixture]
public sealed class InternalGuardUtilitiesTests
{
    /// <summary>
    /// Verifies an active instance does not throw.
    /// </summary>
    [Test]
    public void ThrowIfDisposed_DoesNotThrowWhenActive()
    {
        object instance = new();
        int disposed = 0;

        Assert.DoesNotThrow(() => GuardUtilities.ThrowIfDisposed(instance, ref disposed));
    }

    /// <summary>
    /// Verifies a disposed flag triggers <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public void ThrowIfDisposed_ThrowsWhenDisposed()
    {
        object instance = new();
        int disposed = 1;

        Assert.Throws<ObjectDisposedException>(() => GuardUtilities.ThrowIfDisposed(instance, ref disposed));
    }
}
