// <copyright file="ScramStoredCredential.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: SCRAM stored secret material supplied by the host.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// SCRAM stored keys for a user (RFC 5802); supplied by <see cref="IScramCredentialStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>Hosts typically persist Salt, IterationCount, StoredKey, and ServerKey derived from the password at account provisioning time.</para>
    /// </remarks>
    public sealed class ScramStoredCredential
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScramStoredCredential"/> class.
        /// </summary>
        /// <param name="salt">Salt bytes used in SCRAM.</param>
        /// <param name="iterationCount">PBKDF2 iteration count.</param>
        /// <param name="storedKey">StoredKey = H(ClientKey).</param>
        /// <param name="serverKey">ServerKey from SCRAM derivation.</param>
        public ScramStoredCredential(ReadOnlyMemory<byte> salt, int iterationCount, ReadOnlyMemory<byte> storedKey, ReadOnlyMemory<byte> serverKey)
        {
            if (iterationCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(iterationCount));
            }

            this.Salt = salt;
            this.IterationCount = iterationCount;
            this.StoredKey = storedKey;
            this.ServerKey = serverKey;
        }

        /// <summary>
        /// Gets the salt bytes.
        /// </summary>
        public ReadOnlyMemory<byte> Salt { get; }

        /// <summary>
        /// Gets the PBKDF2 iteration count.
        /// </summary>
        public int IterationCount { get; }

        /// <summary>
        /// Gets the stored client key hash (StoredKey).
        /// </summary>
        public ReadOnlyMemory<byte> StoredKey { get; }

        /// <summary>
        /// Gets the server key used to verify ClientProof.
        /// </summary>
        public ReadOnlyMemory<byte> ServerKey { get; }
    }
}
