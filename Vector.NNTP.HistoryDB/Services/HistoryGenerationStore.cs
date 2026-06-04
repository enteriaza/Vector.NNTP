// <copyright file="HistoryGenerationStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Persists rebuild generation across crashes within a single rebuild attempt.
    /// </summary>
    internal sealed class HistoryGenerationStore
    {
        /// <summary>
        /// The path to the generation file.
        /// </summary>
        private readonly string _generationPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryGenerationStore"/> class.
        /// </summary>
        /// <param name="options">History options.</param>
        public HistoryGenerationStore(IOptions<HistoryDbOptions> options)
        {
            string dir = Path.GetFullPath(options.Value.DbDir);
            this._generationPath = Path.Combine(dir, "history.generation");
        }

        /// <summary>
        /// Allocates a new generation stamp for a fresh rebuild.
        /// </summary>
        /// <returns>Generation value.</returns>
        public ulong AllocateGeneration()
        {
            ulong next = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (File.Exists(this._generationPath) &&
                ulong.TryParse(File.ReadAllText(this._generationPath).Trim(), out ulong existing))
            {
                next = Math.Max(next, existing + 1);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(this._generationPath)!);
            File.WriteAllText(this._generationPath, next.ToString());
            return next;
        }

        /// <summary>
        /// Reads the persisted generation if present.
        /// </summary>
        /// <returns>Generation or null.</returns>
        public ulong? TryReadGeneration()
        {
            if (!File.Exists(this._generationPath))
            {
                return null;
            }

            return ulong.TryParse(File.ReadAllText(this._generationPath).Trim(), out ulong gen) ? gen : null;
        }
    }
}
