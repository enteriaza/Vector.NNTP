// <copyright file="DisposalUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Disposal;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="DisposalUtilities"/> return semantics on success, null, and faulted disposal.
/// </summary>
[TestFixture]
public sealed class DisposalUtilitiesTests
{
    /// <summary>
    /// Verifies null disposal is a no-op that returns no exception.
    /// </summary>
    [Test]
    public void TryDispose_Null_ReturnsNull()
    {
        Assert.That(DisposalUtilities.TryDispose(null), Is.Null);
    }

    /// <summary>
    /// Verifies successful disposal returns null and marks the resource disposed.
    /// </summary>
    [Test]
    public void TryDispose_Success_ReturnsNullAndDisposes()
    {
        ThrowingDisposable resource = new(throwOnDispose: false);

        Exception? ex = DisposalUtilities.TryDispose(resource);

        Assert.That(ex, Is.Null);
        Assert.That(resource.IsDisposed, Is.True);
    }

    /// <summary>
    /// Verifies faulted disposal returns the caught exception without rethrowing.
    /// </summary>
    [Test]
    public void TryDispose_Faulted_ReturnsException()
    {
        ThrowingDisposable resource = new(throwOnDispose: true);

        Exception? ex = DisposalUtilities.TryDispose(resource);

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex, Is.TypeOf<InvalidOperationException>());
    }

    /// <summary>
    /// Test double that optionally throws from <see cref="IDisposable.Dispose"/>.
    /// </summary>
    private sealed class ThrowingDisposable : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingDisposable"/> class.
        /// </summary>
        /// <param name="throwOnDispose">When <see langword="true"/>, <see cref="Dispose"/> throws.</param>
        public ThrowingDisposable(bool throwOnDispose)
        {
            this.ThrowOnDispose = throwOnDispose;
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="Dispose"/> has been called.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets a value indicating whether <see cref="Dispose"/> should throw.
        /// </summary>
        private bool ThrowOnDispose { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            this.IsDisposed = true;

            if (this.ThrowOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }
}
