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
            this._stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this._fillTask = this.FillInputAsync();
            this._drainTask = this.DrainOutputAsync();
        }

        /// <summary>
        /// Gets the pipe reader fed from the stream.
        /// </summary>
        internal PipeReader Input => this._inputPipe.Reader;

        /// <summary>
        /// Gets the pipe writer drained to the stream.
        /// </summary>
        internal PipeWriter Output => this._outputPipe.Writer;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await this._inputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            await this._outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            try
            {
                await this._fillTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Background fill ended with fault or cancel.
            }

            try
            {
                await this._drainTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Background drain ended with fault or cancel.
            }

            await this._inputPipe.Reader.CompleteAsync().ConfigureAwait(false);
            await this._outputPipe.Reader.CompleteAsync().ConfigureAwait(false);
        }

        private async Task FillInputAsync()
        {
            try
            {
                while (true)
                {
                    Memory<byte> memory = this._inputPipe.Writer.GetMemory();
                    int read = await this._stream.ReadAsync(memory).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    this._inputPipe.Writer.Advance(read);
                    FlushResult flush = await this._inputPipe.Writer.FlushAsync().ConfigureAwait(false);
                    if (flush.IsCompleted || flush.IsCanceled)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await this._inputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        private async Task DrainOutputAsync()
        {
            try
            {
                while (true)
                {
                    ReadResult result = await this._outputPipe.Reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    if (!buffer.IsEmpty)
                    {
                        foreach (ReadOnlyMemory<byte> segment in buffer)
                        {
                            await this._stream.WriteAsync(segment).ConfigureAwait(false);
                        }
                    }

                    this._outputPipe.Reader.AdvanceTo(buffer.End);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await this._outputPipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }
}
