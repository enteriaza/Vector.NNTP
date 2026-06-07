// <copyright file="HistoryDbOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// Validates <see cref="HistoryDbOptions"/> at startup.
    /// </summary>
    internal sealed class HistoryDbOptionsValidator : IValidateOptions<HistoryDbOptions>
    {
        /// <summary>
        /// Validates history database paths, shard geometry, and RocksDB tuning coupling at startup.
        /// </summary>
        /// <param name="name">Options name (unused).</param>
        /// <param name="options">Bound options instance to validate.</param>
        /// <returns>
        /// <see cref="ValidateOptionsResult.Success"/> when all constraints pass; otherwise
        /// <see cref="ValidateOptionsResult.Fail(IEnumerable{string})"/> with option-specific messages.
        /// </returns>
        /// <remarks>
        /// Enforces power-of-two <see cref="HistoryDbOptions.MemoryShardCount"/>, non-negative block caches, Bloom and
        /// block-size ranges, and that stats mirroring requires statistics collection with a non-zero dump period.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public ValidateOptionsResult Validate(string? name, HistoryDbOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(options.DbDir))
            {
                return ValidateOptionsResult.Fail($"{nameof(HistoryDbOptions.DbDir)} is required.");
            }

            try
            {
                _ = Path.GetFullPath(options.DbDir);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return ValidateOptionsResult.Fail($"{nameof(HistoryDbOptions.DbDir)} is not a valid path: {ex.Message}");
            }

            return options.RocksDb.MirrorStatsToHostLogger &&
                   (options.RocksDb.StatsDumpPeriodSec == 0 || !options.RocksDb.EnableStatistics)
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.MirrorStatsToHostLogger)} requires {nameof(HistoryRocksDbOptions.EnableStatistics)} = true and {nameof(HistoryRocksDbOptions.StatsDumpPeriodSec)} > 0.")
                : options.RocksDb.StatsDumpPeriodSec > 0 && !options.RocksDb.EnableStatistics
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.StatsDumpPeriodSec)} requires {nameof(HistoryRocksDbOptions.EnableStatistics)} = true for periodic RocksDB LOG dumps.")
                : options.RocksDb.DigestBloomBitsPerKey is < 0 or > 30
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.DigestBloomBitsPerKey)} must be between 0 and 30.")
                : options.RocksDb.ExpirationBloomBitsPerKey is < 0 or > 30
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.ExpirationBloomBitsPerKey)} must be between 0 and 30.")
                : options.RocksDb.BlockSizeBytes is < 0 or > 1_048_576
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.BlockSizeBytes)} must be between 0 and 1048576.")
                : options.RocksDb.ExpirationMemtablePrefixBloomRatio is < 0 or > 0.5
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.ExpirationMemtablePrefixBloomRatio)} must be between 0 and 0.5.")
                : options.RocksDb.DigestBlockCacheBytes < 0
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.DigestBlockCacheBytes)} must be non-negative.")
                : options.RocksDb.ExpirationBlockCacheBytes < 0
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.ExpirationBlockCacheBytes)} must be non-negative.")
                : options.MemoryShardCount is < 1 or > 256
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryDbOptions.MemoryShardCount)} must be between 1 and 256.")
                : (options.MemoryShardCount & (options.MemoryShardCount - 1)) != 0
                ? ValidateOptionsResult.Fail(
                    $"{nameof(HistoryDbOptions.MemoryShardCount)} must be a power of two.")
                : ValidateOptionsResult.Success;
        }
    }
}
