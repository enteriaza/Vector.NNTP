// <copyright file="LoggingDirectoryUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Serilog file sink directory resolution from host JSON configuration.

using Microsoft.Extensions.Configuration;

namespace Vector.NNTP.Utilities.Diagnostics
{
    /// <summary>
    /// Resolves the Serilog rolling-file directory from host configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Configuration key:</b> <c>Logging:LogDir</c> in <c>NNTPD.json</c> / <c>NNRPD.json</c>.</para>
    /// <para>When unset or whitespace, the default is <c>{AppContext.BaseDirectory}/logs</c>.</para>
    /// <para>Relative paths are normalized under <see cref="AppContext.BaseDirectory"/>; absolute paths are used as-is.</para>
    /// </remarks>
    public static class LoggingDirectoryUtilities
    {
        /// <summary>
        /// Configuration section key for logging settings.
        /// </summary>
        public const string SectionName = "Logging";

        /// <summary>
        /// Configuration property key for the Serilog file sink directory.
        /// </summary>
        public const string LogDirKey = "LogDir";

        /// <summary>
        /// Default subdirectory name under <see cref="AppContext.BaseDirectory"/> when <see cref="LogDirKey"/> is unset.
        /// </summary>
        public const string DefaultLogSubdirectory = "logs";

        /// <summary>
        /// Resolves the directory Serilog should use for rolling log files.
        /// </summary>
        /// <param name="configuration">Host configuration after JSON binding.</param>
        /// <returns>
        /// An absolute directory path. Never null or empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        public static string ResolveLogDirectory(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string? configured = configuration[$"{SectionName}:{LogDirKey}"];
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Path.Combine(AppContext.BaseDirectory, DefaultLogSubdirectory);
            }

            string trimmed = configured.Trim();
            return Path.IsPathRooted(trimmed) ? trimmed : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
        }
    }
}
