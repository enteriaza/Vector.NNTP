// AssemblyInfoUtilities.cs — Shared assembly metadata helpers for consistent version and name extraction.
//
// Centralizes entry-assembly name and version extraction via a single static constructor call.
// All values are computed once at class load time and cached in static readonly fields; no per-call
// reflection overhead occurs. This eliminates redundant Assembly.GetEntryAssembly calls and the
// duplicated reflection+source-hash-stripping pattern that existed across multiple call sites.
//
// Thread safety: All fields are static readonly; inherently thread-safe.
// Cross-platform: Fully portable; uses only BCL reflection APIs available on .NET 8 (Windows x64, Linux x64).
// SIMD applicability: Not applicable (one-time string operations at class load time).
//

using System.Reflection;

namespace MessageBus.Utilities
{
    /// <summary>
    /// Shared assembly metadata helpers for consistent application name and version extraction across the project.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Centralizes entry-assembly metadata extraction without duplicating the reflection +
    /// source-hash-stripping logic. Consumers access stable, cached <see cref="ApplicationName"/> and
    /// <see cref="ApplicationVersion"/> values instead of reinventing metadata resolution independently.</para>
    ///
    /// <para><b>Source-hash stripping:</b> SDK-style projects append a <c>+commitHash</c> suffix to the informational
    /// version (e.g., <c>"1.0.0+abc123def"</c>). <see cref="ApplicationVersion"/> strips this suffix for display.
    /// The full unstripped version is available via <see cref="InformationalVersionFull"/> for build-level diagnostics
    /// that need the commit hash.</para>
    ///
    /// <para><b>Fallback strategy:</b> When the entry assembly or version metadata is unavailable (test hosts,
    /// single-file trimmed apps), the class returns <see cref="DefaultApplicationName"/> and <see cref="DefaultVersion"/>
    /// so that logs and diagnostics remain readable with clearly identifiable fallback values.</para>
    /// </remarks>
    internal static class AssemblyInfoUtilities
    {

        #region Constants

        /// <summary>
        /// Default application name used when the entry assembly or its name is unavailable (e.g., when running
        /// under a test host or a single-file trimmed application).
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> <c>"TaskExecutioner"</c>. Chosen to match the assembly name and be clearly
        /// identifiable in logs and diagnostics so operators can detect fallback usage immediately.</para>
        /// </remarks>
        private const string DefaultApplicationName = "TaskExecutioner";

        /// <summary>
        /// Default version string used when no version metadata is available from the entry assembly.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> <c>"0.0.0"</c>. Chosen to be clearly identifiable as a fallback in logs and
        /// diagnostics so operators can diagnose missing or inaccessible version metadata immediately.</para>
        /// </remarks>
        private const string DefaultVersion = "0.0.0";

        #endregion

        #region Fields

        /// <summary>
        /// The entry assembly's short name (e.g., <c>"TaskExecutioner"</c>).  Falls back to <see cref="DefaultApplicationName"/>
        /// if the entry assembly or its name is unavailable (e.g., when running under a test host).
        /// </summary>
        internal static readonly string ApplicationName;

        /// <summary>
        /// The entry assembly's informational version with the source-hash suffix stripped (e.g., <c>"1.0.0"</c> instead
        /// of <c>"1.0.0+abc123def"</c>).  Falls back to the <see cref="AssemblyName.Version"/> three-part string, or
        /// <see cref="DefaultVersion"/> if no version metadata is available.
        /// </summary>
        /// <remarks>
        /// <para><b>Resolution order:</b></para>
        /// <list type="number">
        ///   <item><description><see cref="AssemblyInformationalVersionAttribute.InformationalVersion"/> with
        ///     <c>+hash</c> suffix stripped -- the richest version source (includes prerelease tags like
        ///     <c>-beta.1</c>).</description></item>
        ///   <item><description><see cref="AssemblyName.Version"/> via <c>ToString()</c> -- four-part numeric version
        ///     (<c>Major.Minor.Build.Revision</c>).</description></item>
        ///   <item><description><see cref="DefaultVersion"/> (<c>"0.0.0"</c>) -- defensive fallback when running in
        ///     environments where the entry assembly has no version metadata (test hosts, single-file trimmed
        ///     apps).</description></item>
        /// </list>
        /// </remarks>
        internal static readonly string ApplicationVersion;

        /// <summary>
        /// The entry assembly's full informational version including the source-hash suffix, if present (e.g.,
        /// <c>"1.0.0+abc123def"</c>).  Useful for build-level diagnostics where the exact commit hash is needed.
        /// </summary>
        /// <remarks>
        /// <para><b>Fallback:</b> When the <see cref="AssemblyInformationalVersionAttribute"/> is not present, falls
        /// back to <see cref="AssemblyName.Version"/> (four-part numeric), then to <see cref="DefaultVersion"/>.</para>
        /// </remarks>
        internal static readonly string InformationalVersionFull;

        #endregion

        #region Constructors

        /// <summary>
        /// Static constructor that resolves all assembly metadata fields from a single
        /// <see cref="Assembly.GetEntryAssembly"/> call.
        /// </summary>
        /// <remarks>
        /// <para><b>Single resolution:</b> The entry assembly is resolved exactly once and reused for all field
        /// initializations.  This eliminates the redundant <see cref="Assembly.GetEntryAssembly"/> calls that occurred
        /// when each field was initialised independently via inline initialisers.</para>
        ///
        /// <para><b>Exception safety:</b> <see cref="Assembly.GetEntryAssembly"/> returns <see langword="null"/> (never
        /// throws) when the managed entry point is not available.  All downstream null-conditional chains produce safe
        /// fallback values.  No exceptions can escape this constructor.</para>
        /// </remarks>
        static AssemblyInfoUtilities()
        {
            Assembly? assembly = Assembly.GetEntryAssembly();
            AssemblyName? assemblyName = assembly?.GetName();
            ApplicationName = assemblyName?.Name ?? DefaultApplicationName;
            string? informationalVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string? numericVersion = assemblyName?.Version?.ToString();
            // Full informational version (with +hash suffix) for diagnostics.
            InformationalVersionFull = informationalVersion ?? numericVersion ?? DefaultVersion;
            // Stripped version (without +hash suffix) for display.
            ApplicationVersion = StripSourceHash(informationalVersion) ?? numericVersion ?? DefaultVersion;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Strips the source-hash suffix (<c>+commitHash</c>) appended by SDK-style projects from an informational
        /// version string.
        /// </summary>
        /// <param name="informationalVersion">The raw informational version string, or <see langword="null"/> if the
        /// attribute is not present on the entry assembly.</param>
        /// <returns>The version string without the <c>+hash</c> suffix, or <see langword="null"/> if
        /// <paramref name="informationalVersion"/> is <see langword="null"/>.</returns>
        /// <remarks>
        /// <para><b>Example:</b> <c>"1.0.0-beta.1+abc123def"</c> becomes <c>"1.0.0-beta.1"</c>.  If no <c>+</c>
        /// character is present, the string is returned unchanged.</para>
        ///
        /// <para><b>Null propagation:</b> Returns <see langword="null"/> when the input is <see langword="null"/>,
        /// allowing the caller to fall through to the next resolution step in the version fallback chain.</para>
        /// </remarks>
        private static string? StripSourceHash(string? informationalVersion)
        {
            if (informationalVersion is null)
                return null;
            int plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        }

        #endregion

    }
}
