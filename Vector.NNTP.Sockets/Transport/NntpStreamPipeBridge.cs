// <copyright file="NntpStreamPipeBridge.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bridges a bidirectional Stream to PipeReader/PipeWriter.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Bridges a <see cref="Stream"/> to <see cref="PipeReader"/> and <see cref="PipeWriter"/> for NNTP I/O.
    /// </summary>
    internal sealed class NntpStreamPipeBridge : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly Pipe _inputPipe = new();
        private readonly Pipe _outputPipe = new();
        private readonly Task _fillTask;
        private readonly Task _drainTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpStreamPipeBridge"/> class.
        /// </summary>
        /// <param name="stream">Underlying bidirectional stream.</param>
        public NntpStreamPipeBridge(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _fillTask = FillInputAsync();
            _drainTask = DrainOutputAsync();
        }

        /// <summary>
        /// Gets the pipe reader fed from the stream.
        /// </summary>
        internal PipeReader Input => _inputPipe.Reader;

        /// <summary>
        /// Gets the pipe writer drained to the stream.
        /// </summary>
        internal PipeWriter Output => _outputPipe.Writer;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _inputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            try
            {
                await _fillTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Background fill ended with fault or cancel.
            }

            try
            {
                await _drainTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Background drain ended with fault or cancel.
            }

            await _inputPipe.Reader.CompleteAsync().ConfigureAwait(false);
            await _outputPipe.Reader.CompleteAsync().ConfigureAwait(false);
        }

        private async Task FillInputAsync()
        {
            try
            {
                while (true)
                {
                    Memory<byte> memory = _inputPipe.Writer.GetMemory();
                    int read = await _stream.ReadAsync(memory).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    _inputPipe.Writer.Advance(read);
                    FlushResult flush = await _inputPipe.Writer.FlushAsync().ConfigureAwait(false);
                    if (flush.IsCompleted || flush.IsCanceled)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await _inputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        private async Task DrainOutputAsync()
        {
            try
            {
                while (true)
                {
                    ReadResult result = await _outputPipe.Reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    if (!buffer.IsEmpty)
                    {
                        foreach (ReadOnlyMemory<byte> segment in buffer)
                        {
                            await _stream.WriteAsync(segment).ConfigureAwait(false);
                        }
                    }

                    _outputPipe.Reader.AdvanceTo(buffer.End);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await _outputPipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }
}
