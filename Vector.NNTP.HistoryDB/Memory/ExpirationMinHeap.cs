// <copyright file="ExpirationMinHeap.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: min-heap ordering for memory-cache eviction by expiration epoch.

using Vector.NNTP.HistoryDB.Encoding;

namespace Vector.NNTP.HistoryDB.Memory
{
    /// <summary>
    /// Binary min-heap of <c>(expiration, digest)</c> pairs for O(log n) eviction candidate ordering.
    /// </summary>
    /// <remarks>
    /// <para>Supports lazy tombstones: callers push on every insert/update and discard stale heap tops during
    /// eviction by comparing against the authoritative dictionary expiration.</para>
    /// </remarks>
    internal sealed class ExpirationMinHeap
    {
        /// <summary>
        /// Heap storage ordered by expiration ascending at index 0.
        /// </summary>
        private readonly List<(ulong Expiration, DigestKey Key)> _entries = [];

        /// <summary>
        /// Gets the number of heap entries including tombstones not yet popped.
        /// </summary>
        internal int Count => _entries.Count;

        /// <summary>
        /// Pushes an expiration-ordered eviction candidate.
        /// </summary>
        /// <param name="expiration">Expiration epoch seconds.</param>
        /// <param name="key">Digest key.</param>
        internal void Push(ulong expiration, in DigestKey key)
        {
            _entries.Add((expiration, key));
            SiftUp(_entries.Count - 1);
        }

        /// <summary>
        /// Reads the minimum expiration entry without removing it.
        /// </summary>
        /// <param name="expiration">Expiration epoch at heap root.</param>
        /// <param name="key">Digest key at heap root.</param>
        /// <returns><see langword="true"/> when the heap is non-empty.</returns>
        internal bool TryPeek(out ulong expiration, out DigestKey key)
        {
            if (_entries.Count == 0)
            {
                expiration = 0;
                key = default;
                return false;
            }

            (expiration, key) = _entries[0];
            return true;
        }

        /// <summary>
        /// Removes the minimum expiration entry.
        /// </summary>
        internal void Pop()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            int last = _entries.Count - 1;
            if (last == 0)
            {
                _entries.Clear();
                return;
            }

            _entries[0] = _entries[last];
            _entries.RemoveAt(last);
            SiftDown(0);
        }

        /// <summary>
        /// Clears all heap entries.
        /// </summary>
        internal void Clear()
        {
            _entries.Clear();
        }

        /// <summary>
        /// Moves the entry at <paramref name="index"/> toward the root while its expiration is smaller than its parent.
        /// </summary>
        /// <param name="index">Index to sift up from.</param>
        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_entries[index].Expiration >= _entries[parent].Expiration)
                {
                    break;
                }

                (_entries[index], _entries[parent]) = (_entries[parent], _entries[index]);
                index = parent;
            }
        }

        /// <summary>
        /// Moves the entry at <paramref name="index"/> toward the leaves while it is larger than a child.
        /// </summary>
        /// <param name="index">Index to sift down from.</param>
        private void SiftDown(int index)
        {
            int count = _entries.Count;
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                {
                    break;
                }

                int smallest = left;
                int right = left + 1;
                if (right < count && _entries[right].Expiration < _entries[left].Expiration)
                {
                    smallest = right;
                }

                if (_entries[index].Expiration <= _entries[smallest].Expiration)
                {
                    break;
                }

                (_entries[index], _entries[smallest]) = (_entries[smallest], _entries[index]);
                index = smallest;
            }
        }
    }
}
