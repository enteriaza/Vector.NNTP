// <copyright file="MySqlAuthConnectionStringValidatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Tests for <see cref="MySqlAuthConnectionStringValidator"/>.
    /// </summary>
    [TestFixture]
    public sealed class MySqlAuthConnectionStringValidatorTests
    {
        /// <summary>
        /// Ensures valid connection strings pass validation.
        /// </summary>
        [Test]
        public void ValidateOrThrow_ValidConnectionString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                MySqlAuthOptions options = new MySqlAuthOptions(
                    "Server=127.0.0.1;Database=nntp;User ID=test;Password=secret");
                Assert.That(options.ConnectionString, Does.Contain("Server=127.0.0.1"));
            });
        }

        /// <summary>
        /// Ensures blank server is rejected.
        /// </summary>
        [Test]
        public void ValidateOrThrow_MissingServer_ThrowsArgumentException()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                _ = new MySqlAuthOptions("Database=nntp;User ID=test;Password=secret");
            })!;

            Assert.That(ex.ParamName, Is.EqualTo("connectionString"));
        }

        /// <summary>
        /// Ensures blank database is rejected.
        /// </summary>
        [Test]
        public void ValidateOrThrow_MissingDatabase_ThrowsArgumentException()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                _ = new MySqlAuthOptions("Server=127.0.0.1;User ID=test;Password=secret");
            })!;

            Assert.That(ex.ParamName, Is.EqualTo("connectionString"));
        }

        /// <summary>
        /// Ensures malformed connection strings are rejected.
        /// </summary>
        [Test]
        public void ValidateOrThrow_MalformedConnectionString_ThrowsArgumentException()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                _ = new MySqlAuthOptions("=invalid;==");
            })!;

            Assert.That(ex.ParamName, Is.EqualTo("connectionString"));
        }
    }
}
