// <copyright file="YEncSectionCrcSabctoolsFixtureTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// YEncSectionCrcSabctoolsFixtureTests.cs -- Fixture-driven tests for yEnc CRC validation.

using Vector.NNTP.Filters.YEnc;

namespace Vector.NNTP.Tests.Filters.YEnc
{
    /// <summary>
    /// Runs <see cref="YEncSectionCrc.Validate"/> against every sabctools yEnc fixture with curated pass/fail expectations.
    /// </summary>
    /// <remarks>
    /// <para><b>Why fixtures:</b> yEnc validation is a correctness/security boundary. The sabctools fixture corpus encodes
    /// edge cases (CRC mismatches, truncated sections, malformed metadata) that are easy to regress when optimizing.</para>
    ///
    /// <para><b>Catalog:</b> Expectations live in <see cref="YEncSabctoolsFixtureCatalog"/> so new fixtures require an
    /// explicit mapping (and rationale), not silent omission.</para>
    ///
    /// <para><b>Source:</b> sabctools repository <c>tests/yencfiles</c> directory.</para>
    /// </remarks>
    [TestFixture]
    public sealed class YEncSectionCrcSabctoolsFixtureTests
    {
        /// <summary>
        /// Ensures every <c>.yenc</c> file on disk is listed in <see cref="YEncSabctoolsFixtureCatalog"/> (and vice versa).
        /// </summary>
        [Test]
        public void Catalog_MatchesAllFixtureFilesOnDisk()
        {
            string fixtureDir = GetFixtureDirectory();
            string[] diskFiles = Directory.GetFiles(fixtureDir, "*.yenc")
                .Select(Path.GetFileName)
                .OrderBy(static f => f, StringComparer.Ordinal)
                .ToArray()!;

            string[] catalogFiles = YEncSabctoolsFixtureCatalog.All
                .Select(static e => e.FileName)
                .OrderBy(static f => f, StringComparer.Ordinal)
                .ToArray();

            Assert.That(catalogFiles, Is.EqualTo(diskFiles), "Update YEncSabctoolsFixtureCatalog when adding or removing sabctools fixtures.");
        }

        /// <summary>
        /// Validates each curated fixture against <see cref="YEncSectionCrc.Validate"/>.
        /// </summary>
        /// <param name="expectation">Curated file name and expected validation outcome.</param>
        [TestCaseSource(nameof(AllCataloguedFixtures))]
        public void Validate_MatchesCuratedExpectation(YEncSabctoolsFixtureExpectation expectation)
        {
            byte[] bytes = ReadFixtureBytes(expectation.FileName);
            bool actual = YEncSectionCrc.Validate(bytes);

            Assert.That(
                actual,
                Is.EqualTo(expectation.ExpectedValid),
                "{0}: {1}",
                expectation.FileName,
                expectation.Rationale);
        }

        /// <summary>
        /// Supplies one NUnit case per catalog entry.
        /// </summary>
        /// <returns>Test cases for <see cref="Validate_MatchesCuratedExpectation"/>.</returns>
        private static IEnumerable<TestCaseData> AllCataloguedFixtures()
        {
            foreach (YEncSabctoolsFixtureExpectation expectation in YEncSabctoolsFixtureCatalog.All)
            {
                string suffix = expectation.ExpectedValid ? "Pass" : "Fail";
                yield return new TestCaseData(expectation)
                    .SetName($"{expectation.FileName}_{suffix}");
            }
        }

        /// <summary>
        /// Resolves the fixture directory from test output or the repository tree (IDE runners).
        /// </summary>
        /// <returns>Absolute path to <see cref="YEncSabctoolsFixtureCatalog.FixtureDirectoryRelative"/>.</returns>
        private static string GetFixtureDirectory()
        {
            string baseDir = TestContext.CurrentContext.TestDirectory;
            string path = Path.Combine(baseDir, YEncSabctoolsFixtureCatalog.FixtureDirectoryRelative);
            if (Directory.Exists(path))
            {
                return path;
            }

            return Path.Combine(
                GetRepoRootOrFallback(baseDir),
                "Vector.NNTP.Tests",
                YEncSabctoolsFixtureCatalog.FixtureDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Reads a fixture file from the test output directory (preferred) or from the repository tree (fallback for IDE runners).
        /// </summary>
        /// <param name="fileName">Fixture file name.</param>
        /// <returns>The fixture bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> is <see langword="null"/>.</exception>
        private static byte[] ReadFixtureBytes(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            string path = Path.Combine(GetFixtureDirectory(), fileName);
            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// Walks up from <paramref name="startingDirectory"/> to find the repository root (identified by <c>Vector.NNTP.slnx</c>).
        /// </summary>
        /// <param name="startingDirectory">Starting directory.</param>
        /// <returns>Repository root when found; otherwise <paramref name="startingDirectory"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="startingDirectory"/> is <see langword="null"/>.</exception>
        private static string GetRepoRootOrFallback(string startingDirectory)
        {
            ArgumentNullException.ThrowIfNull(startingDirectory);

            DirectoryInfo? d = new(startingDirectory);
            while (d is not null)
            {
                if (File.Exists(Path.Combine(d.FullName, "Vector.NNTP.slnx")))
                {
                    return d.FullName;
                }

                d = d.Parent;
            }

            return startingDirectory;
        }
    }
}
