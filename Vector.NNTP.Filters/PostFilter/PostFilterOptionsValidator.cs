// <copyright file="PostFilterOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterOptionsValidator.cs -- Validates PostFilterOptions beyond data annotations.

using Microsoft.Extensions.Options;

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Validates <see cref="PostFilterOptions"/> beyond data annotations.
    /// </summary>
    /// <remarks>
    /// <para>Register with <c>services.AddSingleton&lt;IValidateOptions&lt;PostFilterOptions&gt;, PostFilterOptionsValidator&gt;()</c>
    /// and enable <c>ValidateOnStart</c> so misconfiguration fails at host startup.</para>
    /// </remarks>
    public sealed class PostFilterOptionsValidator : IValidateOptions<PostFilterOptions>
    {
        /// <summary>
        /// Validates salt length when client tokens are enabled, regex syntax for <see cref="PostFilterOptions.PublicUserIdPattern"/>,
        /// DNS zone list entries, and required Tor DNS suffix configuration.
        /// </summary>
        /// <param name="name">Options name (unused).</param>
        /// <param name="options">Bound options instance.</param>
        /// <returns><see cref="ValidateOptionsResult.Success"/> or a failure result with a human-readable message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <see cref="PostFilterDnsOptions.TorDnsSuffix"/> must be non-empty at startup regardless of whether Tor checks are
        /// enabled in the current profile — hosts configure a placeholder suffix when Tor filtering is disabled.
        /// </remarks>
        public ValidateOptionsResult Validate(string? name, PostFilterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.HeaderRewrite.AppendClientTokenHeader && options.Salt.Length < 6)
            {
                return ValidateOptionsResult.Fail("PostFilter: Salt must be at least 6 characters when HeaderRewrite.AppendClientTokenHeader is true.");
            }

            if (options.ServerType == PostFilterServerType.Both)
            {
                try
                {
                    _ = new Regex(options.PublicUserIdPattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException ex)
                {
                    return ValidateOptionsResult.Fail($"PostFilter: PublicUserIdPattern is not a valid regex: {ex.Message}");
                }
            }

            foreach (string z in options.Dns.RblZones)
            {
                if (string.IsNullOrWhiteSpace(z))
                {
                    return ValidateOptionsResult.Fail("PostFilter: Dns.RblZones must not contain empty entries.");
                }
            }

            foreach (string z in options.Dns.UriblZones)
            {
                if (string.IsNullOrWhiteSpace(z))
                {
                    return ValidateOptionsResult.Fail("PostFilter: Dns.UriblZones must not contain empty entries.");
                }
            }

            return string.IsNullOrWhiteSpace(options.Dns.TorDnsSuffix)
                ? ValidateOptionsResult.Fail("PostFilter: Dns.TorDnsSuffix must not be empty.")
                : ValidateOptionsResult.Success;
        }
    }
}

