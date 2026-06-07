// <copyright file="ProcStatParserTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Utilities.Metrics;

/// <summary>
/// Tests for <see cref="ProcStatParser"/>.
/// </summary>
[TestFixture]
public sealed class ProcStatParserTests
{
    /// <summary>
    /// Verifies aggregate cpu line parsing.
    /// </summary>
    [Test]
    public void TryParseAggregateCpuJiffies_ParsesBusyAndTotal()
    {
        const string Stat = """
            cpu  100 20 30 400 50 5 6 0 0 0
            cpu0 10 2 3 40 5 0 0 0 0 0
            """;

        bool ok = ProcStatParser.TryParseAggregateCpuJiffies(Stat, out ulong busy, out ulong total);
        Assert.That(ok, Is.True);
        Assert.That(busy, Is.EqualTo(161UL));
        Assert.That(total, Is.EqualTo(611UL));
    }
}
