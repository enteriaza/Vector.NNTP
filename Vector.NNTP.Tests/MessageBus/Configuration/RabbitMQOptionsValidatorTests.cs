// <copyright file="RabbitMQOptionsValidatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.MessageBus.Configuration;

namespace Vector.NNTP.Tests.MessageBus.Configuration;

/// <summary>
/// Tests for <see cref="RabbitMQOptionsValidator"/>.
/// </summary>
[TestFixture]
public sealed class RabbitMQOptionsValidatorTests
{
    /// <summary>
    /// Verifies invalid pool bounds fail validation.
    /// </summary>
    [Test]
    public void Validate_MinConnectionsGreaterThanMax_Fails()
    {
        RabbitMQOptions options = new()
        {
            Hosts = ["localhost"],
            Username = "user",
            Password = "secret-value-123",
            MinConnections = 4,
            MaxConnections = 1,
        };

        RabbitMQOptionsValidator validator = new(NullLogger<RabbitMQOptionsValidator>.Instance);
        ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
        Assert.That(result.Failed, Is.True);
    }
}
