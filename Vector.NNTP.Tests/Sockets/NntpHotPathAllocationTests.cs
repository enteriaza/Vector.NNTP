// <copyright file="NntpHotPathAllocationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: allocation assertions for hot-path helpers.

using System.Text;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Allocation regression tests for transport hot paths.
    /// </summary>
    [TestFixture]
    public sealed class NntpHotPathAllocationTests
    {
        /// <summary>
        /// Verifies verb classification from bytes does not allocate.
        /// </summary>
        [Test]
        public void VerbClassification_Bytes_DoesNotAllocate()
        {
            byte[] line = Encoding.ASCII.GetBytes("CAPABILITIES");
            _ = NntpCommandVerbBytes.Classify(line);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _ = NntpCommandVerbBytes.Classify(line);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.LessThanOrEqualTo(64));
        }
    }
}
