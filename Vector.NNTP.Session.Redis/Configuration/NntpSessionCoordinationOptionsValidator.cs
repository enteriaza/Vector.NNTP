// <copyright file="NntpSessionCoordinationOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Session.Redis.Configuration
{
    /// <summary>
    /// Cross-property validation and startup logging for <see cref="NntpSessionCoordinationOptions"/>.
    /// </summary>
    /// <param name="logger">Logger for validation warnings and success summary.</param>
    /// <param name="hostEnvironment">Optional host environment for production DNS checks.</param>
    public sealed class NntpSessionCoordinationOptionsValidator(
        ILogger<NntpSessionCoordinationOptionsValidator> logger,
        IHostEnvironment? hostEnvironment = null)
        : IValidateOptions<NntpSessionCoordinationOptions>
    {
        /// <summary>
        /// Success logged flag.
        /// </summary>
        private int _successLogged;

        /// <summary>
        /// Warnings logged flag.
        /// </summary>
        private int _warningsLogged;

        /// <summary>
        /// Validates <paramref name="options"/>, emits soft warnings once, and logs a success summary when valid.
        /// </summary>
        /// <param name="name">Options name from the DI container (typically <see cref="Options.DefaultName"/>).</param>
        /// <param name="options">Bound coordination options instance.</param>
        /// <returns><see cref="ValidateOptionsResult.Success"/> when there are no hard errors; otherwise a failure result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public ValidateOptionsResult Validate(string? name, NntpSessionCoordinationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            List<ValidationResult> errors = [];
            options.RunCrossPropertyValidation(logger, hostEnvironment, errors);
            if (Interlocked.Exchange(ref _warningsLogged, 1) == 0)
            {
                options.EmitSoftWarnings(logger);
            }

            if (errors.Count == 0 && Interlocked.Exchange(ref _successLogged, 1) == 0)
            {
                options.EmitValidationSuccessSummary(logger);
            }

            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors.ConvertAll(static e => e.ErrorMessage!));
        }
    }
}
