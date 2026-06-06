// <copyright file="NoOpDnsTxtPropagationProbe.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;

namespace Vector.NNTP.Tests.Encryption
{
    /// <summary>
    /// Test double for <see cref="IDnsTxtPropagationProbe"/> that completes immediately without DNS I/O.
    /// </summary>
    internal sealed class NoOpDnsTxtPropagationProbe : IDnsTxtPropagationProbe
    {
        /// <summary>
        /// Completes immediately without polling authoritative DNS.
        /// </summary>
        /// <param name="records">Challenge TXT records (ignored).</param>
        /// <param name="options">Let's Encrypt options (ignored).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task WaitForTxtRecordsAsync(
            IReadOnlyList<(string RecordName, string ExpectedTxt)> records,
            LetsEncryptOptions options,
            CancellationToken cancellationToken)
        {
            _ = records;
            _ = options;
            return Task.CompletedTask;
        }
    }
}
