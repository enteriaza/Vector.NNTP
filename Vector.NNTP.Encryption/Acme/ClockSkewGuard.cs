// <copyright file="ClockSkewGuard.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Validates local clock skew against the ACME directory HTTP <c>Date</c> header.
    /// </summary>
    internal static class ClockSkewGuard
    {
        /// <summary>
        /// Throws when the absolute skew between UTC now and the ACME directory <c>Date</c> header exceeds <paramref name="maxSkew"/>.
        /// </summary>
        /// <param name="http">HTTP client used for the directory HEAD request.</param>
        /// <param name="directoryUri">ACME directory URI.</param>
        /// <param name="maxSkew">Maximum tolerated skew.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when validation succeeds.</returns>
        /// <exception cref="InvalidOperationException">Thrown when skew exceeds <paramref name="maxSkew"/>.</exception>
        public static async Task AssertSkewAcceptableAsync(
            HttpClient http,
            Uri directoryUri,
            TimeSpan maxSkew,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(directoryUri);

            using HttpRequestMessage request = new(HttpMethod.Head, directoryUri);
            using HttpResponseMessage response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();

            if (response.Headers.Date is DateTimeOffset serverUtc)
            {
                TimeSpan skew = (serverUtc - DateTimeOffset.UtcNow).Duration();
                if (skew > maxSkew)
                {
                    throw new InvalidOperationException(
                        $"System clock skew ({skew}) exceeds the configured maximum ({maxSkew}). Synchronize time (NTP) before using ACME.");
                }
            }
        }
    }
}
