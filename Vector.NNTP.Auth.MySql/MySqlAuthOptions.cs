// <copyright file="MySqlAuthOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: validated MySQL connection settings for NNTP authentication services.

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Validated MySQL connection settings shared by MySQL-backed NNTP authentication services.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by <see cref="ServiceCollectionExtensions.AddNntpMySqlAuth"/> so
    /// <see cref="MySqlUserRecordStore"/> can be registered by type (<c>AddSingleton&lt;INntpUserRecordStore,
    /// MySqlUserRecordStore&gt;</c>) instead of a factory delegate.
    /// </remarks>
    internal sealed class MySqlAuthOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlAuthOptions"/> class.
        /// </summary>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is invalid.</exception>
        internal MySqlAuthOptions(string connectionString)
        {
            MySqlAuthConnectionStringValidator.ValidateOrThrow(connectionString, nameof(connectionString));
            this.ConnectionString = connectionString;
        }

        /// <summary>
        /// Gets the validated MySQL connection string for the <c>nntpusers</c> table.
        /// </summary>
        public string ConnectionString { get; }
    }
}
