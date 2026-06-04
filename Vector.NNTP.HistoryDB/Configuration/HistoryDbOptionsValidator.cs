// <copyright file="HistoryDbOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// Validates <see cref="HistoryDbOptions"/> at startup.
    /// </summary>
    public sealed class HistoryDbOptionsValidator : IValidateOptions<HistoryDbOptions>
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

            if (options.RocksDb.StatsDumpPeriodSec > 0 && !options.RocksDb.EnableStatistics)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(HistoryRocksDbOptions.StatsDumpPeriodSec)} requires {nameof(HistoryRocksDbOptions.EnableStatistics)} = true for periodic RocksDB LOG dumps.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
