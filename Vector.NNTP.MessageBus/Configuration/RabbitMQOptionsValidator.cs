// <copyright file="RabbitMQOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMQOptionsValidator.cs -- IValidateOptions cross-property validation and one-time startup logging for RabbitMQOptions.
//
// Invoked by ValidateOnStart in hosts. Emits soft warnings and a success summary at most once per process via Interlocked
// guards on _warningsLogged and _successLogged.
//
// Thread safety:
//   Validate may run concurrently during startup; banner emission is guarded to run once.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.MessageBus.Configuration
{
    /// <summary>
    /// Cross-property validation and startup logging for <see cref="RabbitMQOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Replaces <see cref="IValidatableObject"/> on the options POCO so validation can use
    /// constructor-injected <see cref="ILogger{T}"/> and <see cref="IHostEnvironment"/> instead of resolving services from
    /// <see cref="ValidationContext"/>.</para>
    ///
    /// <para><b>Registration:</b> <c>services.AddSingleton&lt;IValidateOptions&lt;RabbitMQOptions&gt;&gt;(...)</c> and invoked by
    /// <c>ValidateOnStart()</c> in the host.</para>
    ///
    /// <para><b>Thread safety:</b> <see cref="Validate"/> may be called multiple times during startup; warning and success
    /// banners are emitted at most once per process via <see cref="Interlocked"/> guards on
    /// <see cref="_warningsLogged"/> and <see cref="_successLogged"/>.</para>
    /// </remarks>
    /// <param name="logger">Logger for validation warnings and the startup success summary.</param>
    /// <param name="hostEnvironment">
    /// Optional host environment; when <see cref="IHostEnvironment.IsProduction"/> is <see langword="true"/>, production
    /// host safety checks are enabled. When <see langword="null"/>, production checks are skipped.
    /// </param>
    public sealed class RabbitMQOptionsValidator(ILogger<RabbitMQOptionsValidator> logger, IHostEnvironment? hostEnvironment = null)
        : IValidateOptions<RabbitMQOptions>
    {
        /// <summary>
        /// Guards the validation success banner: <c>0</c> = not yet logged, <c>1</c> = logged.
        /// </summary>
        private int _successLogged;

        /// <summary>
        /// Guards soft-warning emission: <c>0</c> = not yet logged, <c>1</c> = logged.
        /// </summary>
        private int _warningsLogged;

        /// <summary>
        /// Validates <paramref name="options"/>, emits soft warnings once, and logs a success summary when valid.
        /// </summary>
        /// <param name="name">Options name from the DI container (typically <see cref="Options.DefaultName"/>).</param>
        /// <param name="options">Bound RabbitMQ options instance.</param>
        /// <returns><see cref="ValidateOptionsResult.Success"/> when there are no hard errors; otherwise a failure result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public ValidateOptionsResult Validate(string? name, RabbitMQOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            List<ValidationResult> errors = [];
            options.RunCrossPropertyValidation(logger, hostEnvironment, errors);
            if (Interlocked.Exchange(ref _warningsLogged, 1) == 0)
                options.EmitSoftWarnings(logger);
            if (errors.Count == 0 && Interlocked.Exchange(ref _successLogged, 1) == 0)
                options.EmitValidationSuccessSummary(logger);
            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors.ConvertAll(static e => e.ErrorMessage!));
        }
    }
}
