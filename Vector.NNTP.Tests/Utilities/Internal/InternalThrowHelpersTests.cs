// <copyright file="InternalThrowHelpersTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Internal;

namespace Vector.NNTP.Tests.Utilities.Internal;

/// <summary>
/// Tests for <see cref="ThrowHelpers"/> and <see cref="SpanValidationHelpers"/> exception messages and guard semantics.
/// </summary>
[TestFixture]
public sealed class InternalThrowHelpersTests
{
    /// <summary>
    /// Verifies destination-too-short throws use a consistent message shape.
    /// </summary>
    [Test]
    public void DestinationTooShort_ThrowsArgumentExceptionWithLengths()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThrowHelpers.DestinationTooShort(requiredLength: 10, actualLength: 4, paramName: "destination"))!;

        Assert.That(ex.ParamName, Is.EqualTo("destination"));
        Assert.That(ex.Message, Does.Contain("required=10"));
        Assert.That(ex.Message, Does.Contain("actual=4"));
    }

    /// <summary>
    /// Verifies ASCII encode destination throws preserve the encoding-specific suffix.
    /// </summary>
    [Test]
    public void DestinationTooShortForAsciiEncode_IncludesAsciiHint()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThrowHelpers.DestinationTooShortForAsciiEncode(requiredLength: 8, actualLength: 2, paramName: "destination"))!;

        Assert.That(ex.Message, Does.Contain("ASCII encoding requires"));
    }

    /// <summary>
    /// Verifies span validation delegates to throw helpers when length is insufficient.
    /// </summary>
    [Test]
    public void EnsureDestinationLength_ThrowsWhenTooShort()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            SpanValidationHelpers.EnsureDestinationLength(requiredLength: 5, actualLength: 1, paramName: "buf"))!;

        Assert.That(ex.ParamName, Is.EqualTo("buf"));
    }

    /// <summary>
    /// Verifies span validation succeeds without throwing when length is sufficient.
    /// </summary>
    [Test]
    public void EnsureDestinationLength_SucceedsWhenLongEnough()
    {
        Assert.DoesNotThrow(() =>
            SpanValidationHelpers.EnsureDestinationLength(requiredLength: 3, actualLength: 8, paramName: "buf"));
    }
}
