// <copyright file="YEncSabctoolsFixtureExpectation.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// YEncSabctoolsFixtureExpectation.cs -- One sabctools fixture file and its expected CRC validation outcome.

namespace Vector.NNTP.Tests.Filters.YEnc
{
    /// <summary>
    /// Expected outcome of <see cref="Vector.NNTP.Filters.YEnc.YEncSectionCrc.Validate"/> for a sabctools fixture file.
    /// </summary>
    /// <param name="FileName">Fixture file name under <c>TestData/YEnc/sabctools</c>.</param>
    /// <param name="ExpectedValid">When <see langword="true"/>, <see cref="Vector.NNTP.Filters.YEnc.YEncSectionCrc.Validate"/> must return <see langword="true"/>.</param>
    /// <param name="Rationale">Why this expectation differs from sabctools decoder-only tests (CRC gate vs robustness).</param>
    public readonly record struct YEncSabctoolsFixtureExpectation(string FileName, bool ExpectedValid, string Rationale);
}
