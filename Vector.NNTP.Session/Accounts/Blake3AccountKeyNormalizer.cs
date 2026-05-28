// <copyright file="Blake3AccountKeyNormalizer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Accounts
{
    /// <summary>
    /// Derives stable Redis account identifiers from usernames using BLAKE3 over normalized UTF-8 text.
    /// </summary>
    /// <remarks>
    /// Full 256-bit BLAKE3 digest as lowercase hex (64 characters). Changing normalization invalidates existing Redis keys.
    /// </remarks>
    public sealed class Blake3AccountKeyNormalizer : IAccountKeyNormalizer
    {
        /// <summary>
        /// Computes the stable account key for coordination (64-char lowercase hex BLAKE3 digest).
        /// </summary>
        /// <param name="username">Raw username from authentication.</param>
        /// <returns>Account key facet for Redis keys.</returns>
        public string ComputeAccountKey(string username)
        {
            return AccountKeyNormalizer.ComputeAccountKey(username);
        }
    }
}
