// <copyright file="NntpArticleBodyReaderBenchmarks.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: BenchmarkDotNet comparison of line-at-a-time vs chunked article body readers.

using System.Text;
using BenchmarkDotNet.Attributes;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Benchmarks legacy per-line body reads against <see cref="NntpArticleBodyReader"/>.
    /// </summary>
    [MemoryDiagnoser]
    [Category("Benchmark")]
    public sealed class NntpArticleBodyReaderBenchmarks
    {
        private byte[] _wireManyLines = null!;
        private NntpConnectionContext _context = null!;

        /// <summary>
        /// Configures benchmark payloads.
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            StringBuilder sb = new();
            for (int i = 0; i < 200; i++)
            {
                sb.Append("Header-").Append(i).Append(": value\r\n");
            }

            sb.Append("\r\nbody-bytes\r\n.\r\n");
            this._wireManyLines = Encoding.ASCII.GetBytes(sb.ToString());
            this._context = new NntpConnectionContext(
                "bench",
                new IPEndPoint(IPAddress.Loopback, 1),
                new IPEndPoint(IPAddress.Loopback, 2),
                NntpHostRole.Transit,
                "bench-node");
        }

        /// <summary>
        /// Legacy line-at-a-time reader.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the benchmark iteration.</returns>
        [Benchmark(Baseline = true)]
        public async Task LegacyLineAtATime()
        {
            (PipeReader reader, NntpLineReader lineReader) = CreateLineReader(this._wireManyLines);
            _ = await LegacyLineAtATimeBodyReader.ReadBodyAsync(lineReader, CancellationToken.None).ConfigureAwait(false);
            await reader.CompleteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Chunked <see cref="NntpArticleBodyReader"/> path.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the benchmark iteration.</returns>
        [Benchmark]
        public async Task ChunkedArticleBodyReader()
        {
            Pipe pipe = new();
            await WriteAllAsync(pipe.Writer, this._wireManyLines).ConfigureAwait(false);
            _ = await NntpArticleBodyReader.ReadDotStuffedBodyAsync(
                pipe.Reader,
                this._context,
                maxBodyBytes: 0,
                CancellationToken.None).ConfigureAwait(false);
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a line reader backed by a fully written pipe.
        /// </summary>
        /// <param name="wire">Dot-stuffed wire payload.</param>
        /// <returns>Pipe reader and line reader pair.</returns>
        private static (PipeReader Reader, NntpLineReader LineReader) CreateLineReader(byte[] wire)
        {
            Pipe pipe = new();
            WriteAllAsync(pipe.Writer, wire).GetAwaiter().GetResult();
            NntpConnectionContext context = new(
                "bench",
                new IPEndPoint(IPAddress.Loopback, 1),
                new IPEndPoint(IPAddress.Loopback, 2),
                NntpHostRole.Transit,
                "bench-node");
            return (pipe.Reader, new NntpLineReader(pipe.Reader, context));
        }

        /// <summary>
        /// Writes an entire wire payload to a pipe in one flush.
        /// </summary>
        /// <param name="writer">Destination pipe writer.</param>
        /// <param name="bytes">Wire payload.</param>
        /// <returns>A <see cref="Task"/> that completes when the writer is completed.</returns>
        private static async Task WriteAllAsync(PipeWriter writer, byte[] bytes)
        {
            bytes.CopyTo(writer.GetSpan(bytes.Length));
            writer.Advance(bytes.Length);
            await writer.FlushAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }
}
