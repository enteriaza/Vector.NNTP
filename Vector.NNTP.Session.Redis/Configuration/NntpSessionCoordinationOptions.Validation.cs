// <copyright file="NntpSessionCoordinationOptions.Validation.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Net;
using Vector.NNTP.Utilities.Networking;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Session.Redis.Configuration
{
    /// <summary>
    /// Cross-property validation helpers for <see cref="NntpSessionCoordinationOptions"/>.
    /// </summary>
    public sealed partial class NntpSessionCoordinationOptions
    {
        /// <summary>
        /// Normalises string properties and collects hard validation errors.
        /// </summary>
        /// <param name="logger">Logger for production-safety warnings.</param>
        /// <param name="hostEnvironment">Host environment (reserved for future production-only checks).</param>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        internal void RunCrossPropertyValidation(
            ILogger logger,
            IHostEnvironment? hostEnvironment,
            List<ValidationResult> errors)
        {
            _ = hostEnvironment;
            if (Hosts is not null)
            {
                for (int i = 0; i < Hosts.Length; i++)
                {
                    if (Hosts[i] is not null)
                    {
                        Hosts[i] = Hosts[i].Trim();
                    }
                }
            }

            KeyPrefix = KeyPrefix?.Trim() ?? string.Empty;
            if (Hosts is null || Hosts.Length == 0)
            {
                errors.Add(new ValidationResult(
                    "Redis:Hosts must contain at least one host.",
                    [nameof(Hosts)]));
            }

            ValidateHosts(errors, logger);
            ValidatePoolParameters(errors);
        }

        /// <summary>Emits duplicate-host warnings at most once per validation cycle.</summary>
        /// <param name="logger">Logger for advisory warnings.</param>
        internal void EmitSoftWarnings(ILogger logger)
        {
            WarnOnDuplicateHosts(logger);
        }

        /// <summary>Emits validation success summary when there are no hard errors.</summary>
        /// <param name="logger">Logger for the startup banner.</param>
        internal void EmitValidationSuccessSummary(ILogger logger)
        {
            LogValidationSuccess(logger, Hosts?.Length ?? 0, Port, Retry, TimeoutSeconds, MinConnections, MaxConnections);
        }

        /// <summary>Validates pool sizing cross-property invariants.</summary>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        private void ValidatePoolParameters(List<ValidationResult> errors)
        {
            if (MinConnections > MaxConnections)
            {
                errors.Add(new ValidationResult(
                    $"MinConnections ({MinConnections}) must not exceed MaxConnections ({MaxConnections}).",
                    [nameof(MinConnections), nameof(MaxConnections)]));
            }

            if (PoolReconnectBaseDelayMs > PoolReconnectMaxDelayMs)
            {
                errors.Add(new ValidationResult(
                    "PoolReconnectBaseDelayMs must not exceed PoolReconnectMaxDelayMs.",
                    [nameof(PoolReconnectBaseDelayMs), nameof(PoolReconnectMaxDelayMs)]));
            }
        }

        /// <summary>Validates each entry in <see cref="Hosts"/>.</summary>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        /// <param name="logger">Logger (unused; reserved for production advisories).</param>
        private void ValidateHosts(List<ValidationResult> errors, ILogger logger)
        {
            _ = logger;
            if (Hosts is null)
            {
                return;
            }

            for (int i = 0; i < Hosts.Length; i++)
            {
                string host = Hosts[i];
                if (string.IsNullOrWhiteSpace(host))
                {
                    errors.Add(new ValidationResult($"Hosts[{i}] is null or empty.", [nameof(Hosts)]));
                    continue;
                }

                if (HostParsingUtilities.HasUriScheme(host))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] ('{host}') must not contain a URI scheme. Provide only the hostname or IP address.",
                        [nameof(Hosts)]));
                    continue;
                }

                host = HostParsingUtilities.StripIPv6Brackets(host)!;
                Hosts[i] = host;
                if (HostParsingUtilities.HasPortSuffix(host))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] ('{host}') must not include a port suffix; use Redis:Port instead.",
                        [nameof(Hosts)]));
                    continue;
                }

                if (!IPAddress.TryParse(host, out _) && !DnsValidationUtilities.ValidateHost(host, out string? dnsError))
                {
                    errors.Add(new ValidationResult($"Hosts[{i}] ('{host}'): {dnsError}", [nameof(Hosts)]));
                }
            }
        }

        /// <summary>Logs a warning when duplicate host entries are present.</summary>
        /// <param name="logger">Logger for advisory warnings.</param>
        private void WarnOnDuplicateHosts(ILogger logger)
        {
            if (Hosts is null || Hosts.Length < 2)
            {
                return;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Hosts.Length; i++)
            {
                string host = Hosts[i];
                if (!seen.Add(host))
                {
                    LogDuplicateHostWarning(logger, host);
                }
            }
        }
    }
}