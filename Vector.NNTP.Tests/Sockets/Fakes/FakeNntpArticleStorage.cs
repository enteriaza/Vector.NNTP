// <copyright file="FakeNntpArticleStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory reader storage for protocol tests.

using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// In-memory article storage for GROUP/ARTICLE/POST/NEXT/LAST tests.
    /// </summary>
    internal sealed class FakeNntpArticleStorage : INntpArticleStorage
    {
        private readonly Dictionary<string, (long Low, long High, byte[] Body)> _groups = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeNntpArticleStorage"/> class with a sample group.
        /// </summary>
        public FakeNntpArticleStorage()
        {
            byte[] body = "Path: test.local\r\nFrom: test@example.com\r\nSubject: test\r\nMessage-ID: <test1@test.local>\r\n\r\nbody.\r\n"u8.ToArray();
            this._groups["test.local"] = (1, 2, body);
        }

        /// <inheritdoc />
        public ValueTask<NntpGroupInfo?> SelectGroupAsync(string groupName, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<NntpGroupInfo?>(null);
            }

            int count = (int)Math.Max(0, g.High - g.Low + 1);
            return ValueTask.FromResult<NntpGroupInfo?>(new NntpGroupInfo(groupName, count, g.Low, g.High));
        }

        /// <inheritdoc />
        public ValueTask<NntpArticlePayload?> GetArticleAsync(
            string? groupName,
            long? articleNumber,
            string? messageId,
            NntpArticlePart part,
            CancellationToken cancellationToken)
        {
            _ = part;
            _ = cancellationToken;
            if (groupName is null || !this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<NntpArticlePayload?>(null);
            }

            long num = articleNumber ?? g.High;
            if (num < g.Low || num > g.High)
            {
                return ValueTask.FromResult<NntpArticlePayload?>(null);
            }

            return ValueTask.FromResult<NntpArticlePayload?>(new NntpArticlePayload(num, g.Body));
        }

        /// <inheritdoc />
        public ValueTask<NntpPostResult> PostArticleAsync(ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken)
        {
            _ = articleBytes;
            _ = cancellationToken;
            return ValueTask.FromResult(new NntpPostResult(true, "<posted@test.local>"));
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<string>?> ListActiveAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult<IReadOnlyList<string>?>(this._groups.Keys.ToList());
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<long>?> ListGroupAsync(
            string groupName,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<IReadOnlyList<long>?>(null);
            }

            long low = rangeLow ?? g.Low;
            long high = rangeHigh ?? g.High;
            var numbers = new List<long>();
            for (long n = low; n <= high; n++)
            {
                numbers.Add(n);
            }

            return ValueTask.FromResult<IReadOnlyList<long>?>(numbers);
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<string>?> GetOverviewAsync(
            string groupName,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<IReadOnlyList<string>?>(null);
            }

            long low = rangeLow ?? g.Low;
            long high = rangeHigh ?? g.High;
            var lines = new List<string>();
            for (long n = low; n <= high; n++)
            {
                lines.Add($"{n}\ttest subject\tposter@test.local\t<test{n}@test.local>\t123\t456\t7");
            }

            return ValueTask.FromResult<IReadOnlyList<string>?>(lines);
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<string>?> GetHeadersAsync(
            string groupName,
            string headerField,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken)
        {
            _ = headerField;
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<IReadOnlyList<string>?>(null);
            }

            long low = rangeLow ?? g.Low;
            long high = rangeHigh ?? g.High;
            var lines = new List<string>();
            for (long n = low; n <= high; n++)
            {
                lines.Add($"{n} header-value-{n}");
            }

            return ValueTask.FromResult<IReadOnlyList<string>?>(lines);
        }

        /// <inheritdoc />
        public ValueTask<long?> GetNextArticleNumberAsync(
            string groupName,
            long currentArticleNumber,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<long?>(null);
            }

            if (currentArticleNumber >= g.High)
            {
                return ValueTask.FromResult<long?>(-1L);
            }

            return ValueTask.FromResult<long?>(currentArticleNumber + 1);
        }

        /// <inheritdoc />
        public ValueTask<long?> GetPreviousArticleNumberAsync(
            string groupName,
            long currentArticleNumber,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this._groups.TryGetValue(groupName, out (long Low, long High, byte[] Body) g))
            {
                return ValueTask.FromResult<long?>(null);
            }

            if (currentArticleNumber <= g.Low)
            {
                return ValueTask.FromResult<long?>(-1L);
            }

            return ValueTask.FromResult<long?>(currentArticleNumber - 1);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetArticleMessageIdAsync(
            string? groupName,
            long? articleNumber,
            string? messageId,
            CancellationToken cancellationToken)
        {
            _ = groupName;
            _ = articleNumber;
            _ = messageId;
            _ = cancellationToken;
            return ValueTask.FromResult<string?>("<test1@test.local>");
        }
    }
}
