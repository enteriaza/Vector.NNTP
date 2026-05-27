// <copyright file="Program.Configuration.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.NNTPD
{
    /// <summary>
    /// Host-specific JSON configuration loading for the NNTPD worker.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Primary host settings file at the project content root (not committed; see repository <c>.gitignore</c>).
        /// </summary>
        private const string HostConfigurationFileName = "NNTPD.json";

        /// <summary>
        /// Adds <see cref="HostConfigurationFileName"/> to the host configuration pipeline.
        /// </summary>
        /// <param name="builder">Application builder whose <see cref="HostApplicationBuilder.Configuration"/> is extended.</param>
        /// <exception cref="FileNotFoundException">
        /// Thrown when <see cref="HostConfigurationFileName"/> is missing from <see cref="IHostEnvironment.ContentRootPath"/>.
        /// </exception>
        private static void AddHostConfiguration(HostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            string path = Path.Combine(builder.Environment.ContentRootPath, HostConfigurationFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Host configuration file '{HostConfigurationFileName}' was not found in the content root " +
                    $"({builder.Environment.ContentRootPath}). Create or copy this file before starting NNTPD.",
                    path);
            }

            _ = builder.Configuration.AddJsonFile(path, optional: false, reloadOnChange: true);
        }
    }
}
