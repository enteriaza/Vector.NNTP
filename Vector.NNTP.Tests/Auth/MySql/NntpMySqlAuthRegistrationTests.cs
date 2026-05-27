// <copyright file="NntpMySqlAuthRegistrationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: verifies MySQL auth replaces development credential stubs in DI.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vector.NNTP.Auth.MySql;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Hosting;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// DI registration tests for MySQL-backed NNTP authentication host wiring.
    /// </summary>
    [TestFixture]
    public sealed class NntpMySqlAuthRegistrationTests
    {
        /// <summary>
        /// Verifies <c>ConnectionStrings:MainDB</c> registers <see cref="MySqlNntpCredentialValidator"/> over the dev stub.
        /// </summary>
        [Test]
        public void AddNntpMySqlAuthFromHostConfiguration_MainDb_ReplacesDevelopmentValidator()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MainDB"] = "Server=127.0.0.1;Database=nntp;User ID=test;Password=test",
                })
                .Build();

            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddNntpMySqlAuthFromHostConfiguration(configuration);
            services.AddNntpSocketsTransit();

            using ServiceProvider provider = services.BuildServiceProvider();
            INntpCredentialValidator validator = provider.GetRequiredService<INntpCredentialValidator>();
            Assert.That(validator, Is.InstanceOf<MySqlNntpCredentialValidator>());
        }
    }
}
