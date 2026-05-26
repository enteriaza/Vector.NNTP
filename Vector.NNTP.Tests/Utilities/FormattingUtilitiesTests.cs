// <copyright file="FormattingUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="FormattingUtilities"/> dictionary formatting overloads.
/// </summary>
[TestFixture]
public sealed class FormattingUtilitiesTests
{
    /// <summary>
    /// Verifies both dictionary overloads produce identical formatted output.
    /// </summary>
    [Test]
    public void FormatKeyValuePairs_DictionaryOverloads_Match()
    {
        Dictionary<string, object> dictionary = new()
        {
            ["host"] = "broker",
            ["port"] = 5672,
            ["enabled"] = true,
        };

        string fromReadOnly = FormattingUtilities.FormatKeyValuePairs((IReadOnlyDictionary<string, object>)dictionary);
        string fromDictionary = FormattingUtilities.FormatKeyValuePairs((IDictionary<string, object>)dictionary);

        Assert.That(fromDictionary, Is.EqualTo(fromReadOnly));
        Assert.That(fromDictionary, Does.Contain("host=broker"));
        Assert.That(fromDictionary, Does.Contain("port=5672"));
    }
}
