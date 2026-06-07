// <copyright file="GuardUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// GuardUtilities.cs -- Reusable static disposed-state guards for instance types.
//
// HOT PATH: Volatile.Read + isolated throw keeps read loops inline-friendly.
//
// Thread safety:
//   Callers must publish disposed state with Interlocked/Volatile writes.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Internal
{
    /// <summary>
    /// Static guard helpers for instance types that use an integer disposed flag.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — used from stream read entry points; throw path isolated via BCL
    /// <see cref="ObjectDisposedException.ThrowIf(bool, object?)"/>.</para>
    ///
    /// <para><b>Thread safety:</b> Methods are stateless; callers must set the <c>disposedFlag</c> field with
    /// <see cref="Interlocked"/> or <see cref="Volatile"/> writes.</para>
    /// </remarks>
    internal static class GuardUtilities
    {
        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> when <paramref name="disposedFlag"/> is non-zero.
        /// </summary>
        /// <param name="instance">The object instance passed to <see cref="ObjectDisposedException"/>.</param>
        /// <param name="disposedFlag">Disposed flag field (0 = active, non-zero = disposed).</param>
        /// <exception cref="ObjectDisposedException">Thrown when <paramref name="disposedFlag"/> is non-zero.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfDisposed(object instance, ref int disposedFlag)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposedFlag) != 0, instance);
        }
    }
}
