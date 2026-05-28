// <copyright file="IAccountKeyNormalizer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Accounts
{
    /// <summary>
    /// Derives stable Redis account identifiers from usernames.
    /// </summary>
    public interface IAccountKeyNormalizer
    {
        /// <summary>
        /// Computes the stable account key for coordination keys.
        /// </summary>
        /// <param name="username">Raw username from authentication.</param>
        /// <returns>Sixty-four character lowercase hexadecimal BLAKE3 digest.</returns>
        public string ComputeAccountKey(string username);
    }
}
