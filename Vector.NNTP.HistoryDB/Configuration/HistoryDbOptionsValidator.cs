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
        /// Validates the options.
        /// </summary>
        /// <param name="name">The name of the options.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The result of the validation.</returns>
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
                : ValidateOptionsResult.Success;
        }
    }
}
