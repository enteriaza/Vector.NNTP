// <copyright file="EnvironmentUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// EnvironmentUtilities.cs -- Safe hostname resolution with deterministic fallback for containerized environments.

namespace Vector.NNTP.Utilities.Diagnostics
{
    /// <summary>
    /// Safe environment inspection helpers with deterministic fallbacks for containerized and embedded environments.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Provides a single implementation of safe <see cref="Environment.MachineName"/> resolution.
    /// Misconfigured containers can return empty hostnames; some embedded environments can throw
    /// <see cref="InvalidOperationException"/>. Callers receive a guaranteed non-empty string.</para>
    /// </remarks>
    public static class EnvironmentUtilities
    {
        /// <summary>
        /// Fallback hostname used when <see cref="Environment.MachineName"/> returns empty/whitespace or throws.
        /// </summary>
        public const string FallbackHostname = "unknown-host";

        /// <summary>
        /// Resolves the machine hostname from <see cref="Environment.MachineName"/> with a deterministic fallback.
        /// </summary>
        /// <param name="usedFallback">Set to <see langword="true"/> when the fallback value was used.</param>
        /// <returns>A non-empty hostname string.</returns>
        public static string ResolveMachineName(out bool usedFallback)
        {
            string? name = GetSystemHostname();

            if (!string.IsNullOrWhiteSpace(name))
            {
                usedFallback = false;
                return name;
            }

            usedFallback = true;
            return FallbackHostname;
        }

        /// <summary>
        /// Resolves the machine hostname from <see cref="Environment.MachineName"/> with a deterministic fallback.
        /// </summary>
        /// <returns>A non-empty hostname string.</returns>
        public static string ResolveMachineName() => ResolveMachineName(out _);

        /// <summary>
        /// Attempts to retrieve the system hostname from <see cref="Environment.MachineName"/> with exception safety.
        /// </summary>
        /// <returns>The OS-reported hostname, or <see langword="null"/> if the property access failed.</returns>
        private static string? GetSystemHostname()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
