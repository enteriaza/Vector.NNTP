// <copyright file="AccountKeyNormalizer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Blake3;

namespace Vector.NNTP.Session.Accounts
{
    /// <summary>
    /// Derives stable Redis account identifiers from usernames using BLAKE3 over normalized UTF-8 text.
    /// </summary>
    /// <remarks>
    /// Changing normalization or digest encoding invalidates existing Redis keys until sessions roll over.
    /// </remarks>
    public static class AccountKeyNormalizer
    {
        /// <summary>
        /// Computes the stable account key for coordination (64-char lowercase hex BLAKE3 digest).
        /// </summary>
        /// <param name="username">Raw username from authentication.</param>
        /// <returns>Account key facet for Redis keys.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        public static string ComputeAccountKey(string username)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            string norm = username.Trim().ToLowerInvariant();
            byte[] bytes = Encoding.UTF8.GetBytes(norm);
            Hash digest = Hasher.Hash(bytes);
            return Convert.ToHexString(digest.AsSpan()).ToLowerInvariant();
        }
    }
}
