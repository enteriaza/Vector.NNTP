// <copyright file="HistoryGenerationStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Persists rebuild generation across crashes within a single rebuild attempt.
    /// </summary>
    /// <param name="options">History options.</param>
    /// <param name="metrics">Metrics.</param>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryGenerationStore(
        IOptions<HistoryDbOptions> options,
        HistoryMetrics metrics,
        ILogger<HistoryGenerationStore> logger)
    {
        /// <summary>
        /// The path to the generation file.
        /// </summary>
        private readonly string _generationPath = Path.Combine(
            Path.GetFullPath(options.Value.DbDir),
            "history.generation");

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics = metrics;

        /// <summary>
        /// Allocates a new generation stamp for a fresh rebuild.
        /// </summary>
        /// <returns>Generation value.</returns>
        /// <exception cref="IOException">Thrown when generation file I/O fails.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when generation file access is denied.</exception>
        internal ulong AllocateGeneration()
        {
            try
            {
                ulong next = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (File.Exists(_generationPath) &&
                    ulong.TryParse(File.ReadAllText(_generationPath).Trim(), out ulong existing))
                {
                    next = Math.Max(next, existing + 1);
                }

                _ = Directory.CreateDirectory(Path.GetDirectoryName(_generationPath)!);
                File.WriteAllText(_generationPath, next.ToString());
                LogGenerationAllocated(next, _generationPath);
                return next;
            }
            catch (IOException ex)
            {
                _metrics.RecordGenerationIoError();
                LogGenerationIoFailed(ex, _generationPath);
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                _metrics.RecordGenerationIoError();
                LogGenerationIoFailed(ex, _generationPath);
                throw;
            }
        }

        /// <summary>
        /// Reads the persisted generation if present.
        /// </summary>
        /// <returns>Generation or null when missing or unreadable.</returns>
        internal ulong? TryReadGeneration()
        {
            if (!File.Exists(_generationPath))
            {
                return null;
            }

            try
            {
                return ulong.TryParse(File.ReadAllText(_generationPath).Trim(), out ulong gen) ? gen : null;
            }
            catch (IOException ex)
            {
                _metrics.RecordGenerationIoError();
                LogGenerationIoFailed(ex, _generationPath);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _metrics.RecordGenerationIoError();
                LogGenerationIoFailed(ex, _generationPath);
                return null;
            }
        }
    }
}
