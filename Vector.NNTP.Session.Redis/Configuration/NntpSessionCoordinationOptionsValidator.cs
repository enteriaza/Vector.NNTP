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
        private int _successLogged;
        private int _warningsLogged;

        /// <inheritdoc />
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
