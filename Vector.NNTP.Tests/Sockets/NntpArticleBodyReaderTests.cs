// <copyright file="NntpArticleBodyReaderTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Unit tests for <see cref="NntpArticleBodyReader"/> dot-stuffing and chunked pipe reads.
    /// </summary>
    [TestFixture]
    public sealed class NntpArticleBodyReaderTests
    {
        /// <summary>
        /// Empty body (immediate dot terminator) decodes to empty bytes.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task EmptyBody_ReturnsEmpty()
        {
            byte[] body = await ReadWireAsync(".\r\n", chunkSize: 1).ConfigureAwait(false);
            Assert.That(body, Is.Empty);
        }

        /// <summary>
        /// Multi-line body round-trips with CRLF reinserted.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task MultiLineBody_PreservesContent()
        {
            const string Expected = "Subject: test\r\nMessage-ID: <a@b>\r\n\r\nhello\r\n";
            string wire = "Subject: test\r\nMessage-ID: <a@b>\r\n\r\nhello\r\n.\r\n";
            byte[] body = await ReadWireAsync(wire, chunkSize: 3).ConfigureAwait(false);
            Assert.That(Encoding.ASCII.GetString(body), Is.EqualTo(Expected));
        }

        /// <summary>
        /// Dot-stuffed lines decode to a single leading period.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task DotStuffedLine_DecodesLeadingPeriod()
        {
            const string Expected = ".leader\r\n";
            string wire = "..leader\r\n.\r\n";
            byte[] body = await ReadWireAsync(wire, chunkSize: 5).ConfigureAwait(false);
            Assert.That(Encoding.ASCII.GetString(body), Is.EqualTo(Expected));
        }

        /// <summary>
        /// Bodies exceeding the configured maximum article size return <see cref="NntpArticleBodyReadStatus.ExceededMaxSize"/>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ExceedsMaxArtSize_ReturnsExceededStatus()
        {
            string wire = "line\r\n.\r\n";
            NntpArticleBodyReadResult read = await ReadWireResultAsync(wire, chunkSize: 2, maxBodyBytes: 2)
                .ConfigureAwait(false);
            Assert.That(read.Status, Is.EqualTo(NntpArticleBodyReadStatus.ExceededMaxSize));
        }

        /// <summary>
        /// Legacy and optimized readers produce identical output for the same wire bytes.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task OptimizedReader_MatchesLegacyLineReader()
        {
            string wire = "From: x@y\r\n..dotline\r\n\r\npayload\r\n.\r\n";
            (PipeReader readerA, PipeReader readerB, NntpConnectionContext contextA, NntpConnectionContext contextB, Task writeTaskA, Task writeTaskB) =
                CreateReaderPair(wire, chunkSize: 7);

            NntpLineReader lineReaderA = new(readerA, contextA);
            byte[] legacy = await LegacyLineAtATimeBodyReader.ReadBodyAsync(lineReaderA, CancellationToken.None)
                .ConfigureAwait(false);
            NntpArticleBodyReadResult optimized = await NntpArticleBodyReader.ReadDotStuffedBodyAsync(
                readerB,
                contextB,
                maxBodyBytes: 0,
                CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(writeTaskA, writeTaskB).ConfigureAwait(false);
            Assert.That(optimized.Status, Is.EqualTo(NntpArticleBodyReadStatus.Complete));
            Assert.That(optimized.Body, Is.EqualTo(legacy));
        }

        /// <summary>
        /// Drain path consumes an oversize pipelined body after <see cref="NntpArticleBodyReadStatus.ExceededMaxSize"/>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task DrainAfterMaxArtSizeExceeded_LeavesNextCommandReadable()
        {
            string wire = new string('x', 64) + "\r\n.\r\nCHECK <next@id>\r\n";
            (Pipe pipe, NntpConnectionContext context, Task writeTask) = CreatePipe(wire, chunkSize: 16);

            NntpArticleBodyReadResult read = await NntpArticleBodyReader.ReadDotStuffedBodyAsync(
                pipe.Reader,
                context,
                maxBodyBytes: 8,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Status, Is.EqualTo(NntpArticleBodyReadStatus.ExceededMaxSize));

            await NntpArticleBodyReader.DrainDotStuffedBodyAsync(pipe.Reader, context, CancellationToken.None)
                .ConfigureAwait(false);
            await writeTask.ConfigureAwait(false);

            NntpLineReader lineReader = new(pipe.Reader, context);
            NntpByteLineReadResult next =
                await lineReader.ReadLineBytesAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(next.IsCompleted, Is.False);
            Assert.That(Encoding.ASCII.GetString(next.Line.Span), Is.EqualTo("CHECK <next@id>"));
        }

        /// <summary>
        /// Allocation count for optimized reader does not grow linearly with line count for fixed body size.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task OptimizedReader_AllocationsIndependentOfLineCount()
        {
            int lineCount = 200;
            string wire = BuildManyShortLines(lineCount) + ".\r\n";
            long fewLines = await MeasureOptimizedAllocationsAsync(BuildManyShortLines(5) + ".\r\n").ConfigureAwait(false);
            long manyLines = await MeasureOptimizedAllocationsAsync(wire).ConfigureAwait(false);
            Assert.That(manyLines - fewLines, Is.LessThan(4096));
        }

        /// <summary>
        /// Builds a wire payload with many short CRLF lines for allocation comparisons.
        /// </summary>
        /// <param name="count">Number of lines.</param>
        /// <returns>Wire bytes without the terminating dot line.</returns>
        private static string BuildManyShortLines(int count)
        {
            StringBuilder sb = new();
            for (int i = 0; i < count; i++)
            {
                _ = sb.Append("x\r\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Measures heap allocations for one optimized body read.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <returns>Allocated bytes for the read operation.</returns>
        private static async Task<long> MeasureOptimizedAllocationsAsync(string wire)
        {
            (Pipe pipe, NntpConnectionContext context, Task writeTask) = CreatePipe(wire, chunkSize: 64);
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = await NntpArticleBodyReader.ReadDotStuffedBodyAsync(pipe.Reader, context, 0, CancellationToken.None)
                .ConfigureAwait(false);
            await writeTask.ConfigureAwait(false);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// Feeds fragmented wire bytes into a pipe and reads the decoded body.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <param name="chunkSize">Maximum bytes written per flush.</param>
        /// <param name="maxBodyBytes">Optional body size limit.</param>
        /// <returns>Decoded body bytes.</returns>
        private static async Task<byte[]> ReadWireAsync(string wire, int chunkSize, long maxBodyBytes = 0)
        {
            NntpArticleBodyReadResult read = await ReadWireResultAsync(wire, chunkSize, maxBodyBytes).ConfigureAwait(false);
            Assert.That(read.Status, Is.EqualTo(NntpArticleBodyReadStatus.Complete));
            return read.Body;
        }

        /// <summary>
        /// Feeds fragmented wire bytes into a pipe and returns the body read result.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <param name="chunkSize">Maximum bytes written per flush.</param>
        /// <param name="maxBodyBytes">Optional body size limit.</param>
        /// <returns>Body read result.</returns>
        private static async Task<NntpArticleBodyReadResult> ReadWireResultAsync(string wire, int chunkSize, long maxBodyBytes = 0)
        {
            (Pipe pipe, NntpConnectionContext context, Task writeTask) = CreatePipe(wire, chunkSize);
            NntpArticleBodyReadResult read = await NntpArticleBodyReader.ReadDotStuffedBodyAsync(
                pipe.Reader,
                context,
                maxBodyBytes,
                CancellationToken.None).ConfigureAwait(false);
            await writeTask.ConfigureAwait(false);
            return read;
        }

        /// <summary>
        /// Creates a pipe with a background fragmented writer for the wire payload.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <param name="chunkSize">Maximum bytes written per flush.</param>
        /// <returns>Pipe, connection context, and writer task.</returns>
        private static (Pipe Pipe, NntpConnectionContext Context, Task WriteTask) CreatePipe(string wire, int chunkSize)
        {
            Pipe pipe = new();
            Task writeTask = WriteFragmentedAsync(pipe.Writer, Encoding.ASCII.GetBytes(wire), chunkSize);
            return (pipe, CreateContext(), writeTask);
        }

        /// <summary>
        /// Creates two independent pipes fed with the same fragmented wire payload.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <param name="chunkSize">Maximum bytes written per flush.</param>
        /// <returns>Readers, contexts, and writer tasks for both pipes.</returns>
        private static (PipeReader ReaderA, PipeReader ReaderB, NntpConnectionContext ContextA, NntpConnectionContext ContextB, Task WriteTaskA, Task WriteTaskB)
            CreateReaderPair(string wire, int chunkSize)
        {
            (Pipe pipeA, NntpConnectionContext contextA, Task writeTaskA) = CreatePipe(wire, chunkSize);
            (Pipe pipeB, NntpConnectionContext contextB, Task writeTaskB) = CreatePipe(wire, chunkSize);
            return (pipeA.Reader, pipeB.Reader, contextA, contextB, writeTaskA, writeTaskB);
        }

        /// <summary>
        /// Creates a loopback transit connection context for pipe tests.
        /// </summary>
        /// <returns>Connection context with synthetic endpoints.</returns>
        private static NntpConnectionContext CreateContext()
        {
            return new NntpConnectionContext(
                Guid.NewGuid().ToString("N"),
                new IPEndPoint(IPAddress.Loopback, 1),
                new IPEndPoint(IPAddress.Loopback, 2),
                NntpHostRole.Transit,
                "test-node");
        }

        /// <summary>
        /// Writes bytes to a pipe in small chunks to exercise multi-segment reads.
        /// </summary>
        /// <param name="writer">Destination pipe writer.</param>
        /// <param name="bytes">Wire payload.</param>
        /// <param name="chunkSize">Maximum bytes written per flush.</param>
        /// <returns>A <see cref="Task"/> that completes when the writer is completed.</returns>
        private static async Task WriteFragmentedAsync(PipeWriter writer, byte[] bytes, int chunkSize)
        {
            for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, bytes.Length - offset);
                FlushResult writeResult = await writer.WriteAsync(bytes.AsMemory(offset, count)).ConfigureAwait(false);
                if (writeResult.IsCanceled)
                {
                    break;
                }

                FlushResult flushResult = await writer.FlushAsync().ConfigureAwait(false);
                if (flushResult.IsCanceled)
                {
                    break;
                }
            }

            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }
}
