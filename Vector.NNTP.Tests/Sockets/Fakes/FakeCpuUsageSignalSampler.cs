// <copyright file="FakeCpuUsageSignalSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// Test double returning configured utilization samples.
    /// </summary>
    internal sealed class FakeCpuUsageSignalSampler : ICpuUsageSignalSampler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FakeCpuUsageSignalSampler"/> class.
        /// </summary>
        /// <param name="signalName">Signal name.</param>
        /// <param name="samplePercent">Percent returned on each sample.</param>
        public FakeCpuUsageSignalSampler(string signalName, double samplePercent)
        {
            this.SignalName = signalName;
            this.SamplePercent = samplePercent;
        }

        /// <inheritdoc />
        public string SignalName { get; }

        /// <inheritdoc />
        public bool IsAvailable => true;

        /// <summary>
        /// Gets or sets the sample percent returned by <see cref="TrySample"/>.
        /// </summary>
        public double SamplePercent { get; set; }

        /// <inheritdoc />
        public bool TrySample(out double utilizationPercent)
        {
            utilizationPercent = this.SamplePercent;
            return true;
        }
    }
}
