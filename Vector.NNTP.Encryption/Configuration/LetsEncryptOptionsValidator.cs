// <copyright file="LetsEncryptOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// LetsEncryptOptionsValidator.cs -- IValidateOptions implementation for LetsEncryptOptions.

using System.ComponentModel.DataAnnotations;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Validates <see cref="LetsEncryptOptions"/> when Let's Encrypt is enabled.
    /// </summary>
    internal sealed class LetsEncryptOptionsValidator : IValidateOptions<LetsEncryptOptions>
    {
        /// <summary>
        /// Validates the <see cref="LetsEncryptOptions"/> when Let's Encrypt is enabled.
        /// </summary>
        /// <param name="name">The name of the options.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The result of the validation.</returns>
        public ValidateOptionsResult Validate(string? name, LetsEncryptOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!options.Enabled)
                return ValidateOptionsResult.Success;

            List<ValidationResult> errors = [];

            options.CertDir = options.CertDir?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.CertDir))
            {
                errors.Add(new ValidationResult(
                    "CertDir is required when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.CertDir)]));
            }

            options.AcmeAccountEmail = options.AcmeAccountEmail?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.AcmeAccountEmail) || !options.AcmeAccountEmail.Contains('@', StringComparison.Ordinal))
            {
                errors.Add(new ValidationResult(
                    "AcmeAccountEmail is required and must be a valid email address when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.AcmeAccountEmail)]));
            }

            options.CloudflareApiToken = options.CloudflareApiToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.CloudflareApiToken))
            {
                errors.Add(new ValidationResult(
                    "CloudflareApiToken is required when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.CloudflareApiToken)]));
            }
            else if (CredentialPlaceholderDetector.IsPlaceholder(options.CloudflareApiToken))
            {
                errors.Add(new ValidationResult(
                    "CloudflareApiToken appears to be a template placeholder.",
                    [nameof(LetsEncryptOptions.CloudflareApiToken)]));
            }

            options.CloudflareZoneId = options.CloudflareZoneId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.CloudflareZoneId))
            {
                errors.Add(new ValidationResult(
                    "CloudflareZoneId is required when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.CloudflareZoneId)]));
            }

            options.DomainNames ??= [];
            int writeIndex = 0;
            for (int i = 0; i < options.DomainNames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(options.DomainNames[i]))
                    continue;

                options.DomainNames[writeIndex++] = options.DomainNames[i].Trim().TrimEnd('.');
            }

            if (writeIndex < options.DomainNames.Length)
            {
                string[] compacted = options.DomainNames;
                Array.Resize(ref compacted, writeIndex);
                options.DomainNames = compacted;
            }

            if (options.DomainNames.Length == 0)
            {
                errors.Add(new ValidationResult(
                    "At least one domain name is required when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.DomainNames)]));
            }

            options.NormaliseAndValidateAccountKeyPem(errors);

            options.ClusterBroadcastExchange = options.ClusterBroadcastExchange?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.ClusterBroadcastExchange))
            {
                errors.Add(new ValidationResult(
                    "ClusterBroadcastExchange must not be empty when Let's Encrypt is enabled.",
                    [nameof(LetsEncryptOptions.ClusterBroadcastExchange)]));
            }

            if (options.ClusterEnabled
                && CredentialPlaceholderDetector.IsPlaceholder(options.ClusterBroadcastSigningSecret))
            {
                errors.Add(new ValidationResult(
                    "ClusterBroadcastSigningSecret is required and must not be a placeholder when ClusterEnabled is true.",
                    [nameof(LetsEncryptOptions.ClusterBroadcastSigningSecret)]));
            }

            options.ClusterBroadcastSigningSecretPrevious = options.ClusterBroadcastSigningSecretPrevious?.Trim();
            if (!string.IsNullOrWhiteSpace(options.ClusterBroadcastSigningSecretPrevious)
                && CredentialPlaceholderDetector.IsPlaceholder(options.ClusterBroadcastSigningSecretPrevious))
            {
                errors.Add(new ValidationResult(
                    "ClusterBroadcastSigningSecretPrevious appears to be a template placeholder.",
                    [nameof(LetsEncryptOptions.ClusterBroadcastSigningSecretPrevious)]));
            }

            options.PfxExportPassword = options.PfxExportPassword?.Trim();
            if (!string.IsNullOrWhiteSpace(options.PfxExportPassword)
                && CredentialPlaceholderDetector.IsPlaceholder(options.PfxExportPassword))
            {
                errors.Add(new ValidationResult(
                    "PfxExportPassword appears to be a template placeholder.",
                    [nameof(LetsEncryptOptions.PfxExportPassword)]));
            }

            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors.Select(static e => e.ErrorMessage ?? "Validation failed."));
        }
    }
}
