// <copyright file="SpamAssassinOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: normalizes legacy single-host SpamAssassin configuration.

using Microsoft.Extensions.Options;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Validates and normalizes <see cref="SpamAssassinOptions"/> beyond data annotations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copies legacy <see cref="SpamAssassinOptions.Host"/> into <see cref="SpamAssassinOptions.Hosts"/> when the array
    /// is empty so existing single-host JSON continues to bind. Trims whitespace on all host entries and ensures
    /// <see cref="SpamAssassinOptions.Host"/> mirrors the first round-robin entry when unset.
    /// </para>
    /// <para>Register with <see cref="DependencyInjection.ServiceCollectionExtensions.AddSpamAssassin"/>.</para>
    /// </remarks>
    public sealed class SpamAssassinOptionsValidator : IValidateOptions<SpamAssassinOptions>
    {
        /// <summary>
        /// Normalizes host lists and rejects empty spamd endpoint configuration.
        /// </summary>
        /// <param name="name">Options name from the options manager (unused).</param>
        /// <param name="options">Bound options instance to validate and mutate in place.</param>
        /// <returns>
        /// <see cref="ValidateOptionsResult.Success"/> when at least one host is available after normalization; otherwise a
        /// failure message describing the binding problem.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public ValidateOptionsResult Validate(string? name, SpamAssassinOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.Hosts is null || options.Hosts.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(options.Host))
                {
                    return ValidateOptionsResult.Fail("SpamAssassin:Hosts must contain at least one spamd host.");
                }

                options.Hosts = [options.Host.Trim()];
            }
            else
            {
                for (int i = 0; i < options.Hosts.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(options.Hosts[i]))
                    {
                        return ValidateOptionsResult.Fail($"SpamAssassin:Hosts[{i}] must not be empty.");
                    }

                    options.Hosts[i] = options.Hosts[i].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(options.Host))
            {
                options.Host = options.Hosts[0];
            }

            return ValidateOptionsResult.Success;
        }
    }
}
