// <copyright file="ClockSkewTtlCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Thread-safe short TTL cache so concurrent ACME flows do not spam the directory with HEAD requests.
    /// </summary>
    internal static class ClockSkewTtlCache
    {
        private static readonly object Gate = new();
        private static DateTimeOffset? _lastSuccessUtc;
        private static string? _lastDirectoryUri;

        /// <summary>
        /// Returns true when a recent successful skew check applies to <paramref name="directoryUri"/>.
        /// </summary>
        /// <param name="directoryUri">ACME directory URI.</param>
        /// <param name="ttl">Reuse window.</param>
        /// <returns><see langword="true"/> when a cached success is still valid.</returns>
        public static bool TryHit(Uri directoryUri, TimeSpan ttl)
        {
            string key = directoryUri.AbsoluteUri;
            lock (Gate)
            {
                return _lastSuccessUtc is not null && _lastDirectoryUri == key && DateTimeOffset.UtcNow - _lastSuccessUtc.Value < ttl;
            }
        }

        /// <summary>
        /// Records a successful skew validation for <paramref name="directoryUri"/>.
        /// </summary>
        /// <param name="directoryUri">ACME directory URI.</param>
        public static void RecordSuccess(Uri directoryUri)
        {
            lock (Gate)
            {
                _lastDirectoryUri = directoryUri.AbsoluteUri;
                _lastSuccessUtc = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Clears cached skew state (unit tests).
        /// </summary>
        internal static void ClearForTests()
        {
            lock (Gate)
            {
                _lastDirectoryUri = null;
                _lastSuccessUtc = null;
            }
        }
    }
}
