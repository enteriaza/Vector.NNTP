// <copyright file="AssemblyInfoUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// AssemblyInfoUtilities.cs -- Cached entry-assembly name and version resolution.

using System.Reflection;

namespace Vector.NNTP.Utilities.Diagnostics
{
    /// <summary>
    /// Cached entry-assembly metadata helpers for consistent application name and version extraction.
    /// </summary>
    /// <remarks>
    /// <para><b>Source-hash stripping:</b> SDK-style projects append a <c>+commitHash</c> suffix to the informational
    /// version; <see cref="ApplicationVersion"/> strips it for display, while <see cref="InformationalVersionFull"/>
    /// preserves it for diagnostics.</para>
    /// </remarks>
    public static class AssemblyInfoUtilities
    {
        /// <summary>
        /// The entry assembly's short name, or a deterministic fallback.
        /// </summary>
        public static readonly string ApplicationName;

        /// <summary>
        /// Display version string with any <c>+hash</c> suffix stripped.
        /// </summary>
        public static readonly string ApplicationVersion;

        /// <summary>
        /// Full informational version string (may include <c>+hash</c> suffix).
        /// </summary>
        public static readonly string InformationalVersionFull;

        private const string DefaultApplicationName = "TaskExecutioner";
        private const string DefaultVersion = "0.0.0";

        /// <summary>
        /// Initializes static members of the <see cref="AssemblyInfoUtilities"/> class.
        /// </summary>
        static AssemblyInfoUtilities()
        {
            Assembly? assembly = Assembly.GetEntryAssembly();
            AssemblyName? assemblyName = assembly?.GetName();

            ApplicationName = assemblyName?.Name ?? DefaultApplicationName;

            string? informationalVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string? numericVersion = assemblyName?.Version?.ToString();

            InformationalVersionFull = informationalVersion ?? numericVersion ?? DefaultVersion;
            ApplicationVersion = StripSourceHash(informationalVersion) ?? numericVersion ?? DefaultVersion;
        }

        /// <summary>
        /// Strips an SDK informational-version source hash suffix (<c>+commitHash</c>) when present.
        /// </summary>
        /// <param name="informationalVersion">Raw informational version, or <see langword="null"/>.</param>
        /// <returns>Version without <c>+hash</c>, or <see langword="null"/> if input is <see langword="null"/>.</returns>
        private static string? StripSourceHash(string? informationalVersion)
        {
            if (informationalVersion is null)
            {
                return null;
            }

            int plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        }
    }
}
