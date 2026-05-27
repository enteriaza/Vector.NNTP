// <copyright file="FakeScramCredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: test double for SCRAM capability advertisement.

using System.Diagnostics.CodeAnalysis;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// Minimal <see cref="IScramCredentialStore"/> fake used to verify CAPABILITIES advertisement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fake intentionally does not return credentials; its purpose is to provide a non-null store so the server
    /// advertises SCRAM mechanisms in CAPABILITIES.
    /// </para>
    /// </remarks>
    internal sealed class FakeScramCredentialStore : IScramCredentialStore
    {
        /// <inheritdoc />
        public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
        {
            _ = username;
            credential = null;
            return false;
        }
    }
}
