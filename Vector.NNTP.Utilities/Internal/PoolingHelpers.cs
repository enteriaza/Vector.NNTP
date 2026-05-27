// <copyright file="PoolingHelpers.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// PoolingHelpers.cs -- ArrayPool byte buffer rent/return wrappers for stream copy paths.
//
// HOT PATH: rent/return only; callers own read/write loops and limit enforcement.
//
// Thread safety:
//   ArrayPool.Shared is thread-safe on .NET 8+.

using System.Buffers;

namespace Vector.NNTP.Utilities.Internal
{
    /// <summary>
    /// Thin wrappers around <see cref="ArrayPool{T}.Shared"/> for byte buffer copy paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — eliminates per-call <c>new byte[]</c> in
    /// <see cref="Stream.CopyTo"/> overrides. Buffers are not cleared on return (callers overwrite via reads).</para>
    ///
    /// <para><b>Thread safety:</b> <see cref="ArrayPool{T}.Shared"/> is thread-safe.</para>
    /// </remarks>
    internal static class PoolingHelpers
    {
        /// <summary>
        /// Rents a byte buffer of at least <paramref name="minimumLength"/> from the shared pool.
        /// </summary>
        /// <param name="minimumLength">Minimum buffer length required.</param>
        /// <returns>A rented buffer that may be larger than <paramref name="minimumLength"/>.</returns>
        public static byte[] RentByteBuffer(int minimumLength)
        {
            return ArrayPool<byte>.Shared.Rent(minimumLength);
        }

        /// <summary>
        /// Returns a rented byte buffer to the shared pool.
        /// </summary>
        /// <param name="buffer">The buffer previously obtained from <see cref="RentByteBuffer"/>.</param>
        public static void ReturnByteBuffer(byte[] buffer)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
