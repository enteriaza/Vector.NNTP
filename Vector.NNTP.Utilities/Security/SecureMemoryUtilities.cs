// <copyright file="SecureMemoryUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// SecureMemoryUtilities.cs — Best-effort clearing for sensitive byte buffers.

using System.Security.Cryptography;

namespace Vector.NNTP.Utilities.Security;

/// <summary>
/// Best-effort secure memory clearing for mutable <see cref="byte"/> arrays containing sensitive material.
/// </summary>
/// <remarks>
/// <para><b>Why:</b> <see cref="CryptographicOperations.ZeroMemory"/> uses a clearing routine that the runtime cannot
/// legally optimise away, unlike <see cref="Span{T}.Clear"/> which can be elided as a dead store.</para>
/// </remarks>
public static class SecureMemoryUtilities
{
    /// <summary>
    /// Zeroes one or more buffers via <see cref="CryptographicOperations.ZeroMemory"/>.
    /// </summary>
    /// <param name="buffers">Buffers to clear. <see langword="null"/> elements are skipped.</param>
    public static void ZeroBuffers(params ReadOnlySpan<byte[]?> buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            byte[]? buffer = buffers[i];
            if (buffer is not null)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }
}
