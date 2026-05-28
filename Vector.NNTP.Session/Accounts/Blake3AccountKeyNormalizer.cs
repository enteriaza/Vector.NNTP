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
        /// <inheritdoc />
        public string ComputeAccountKey(string username)
        {
            return AccountKeyNormalizer.ComputeAccountKey(username);
        }
    }
}
