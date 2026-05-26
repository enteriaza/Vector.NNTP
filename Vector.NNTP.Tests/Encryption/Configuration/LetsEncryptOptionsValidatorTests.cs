// <copyright file="LetsEncryptOptionsValidatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Tests.Encryption.Configuration;

/// <summary>
/// Tests for <see cref="LetsEncryptOptionsValidator"/>.
/// </summary>
[TestFixture]
public sealed class LetsEncryptOptionsValidatorTests
{
    private readonly LetsEncryptOptionsValidator _validator = new();

    /// <summary>
    /// Verifies disabled options skip validation.
    /// </summary>
    [Test]
    public void Validate_WhenDisabled_SucceedsWithEmptyFields()
    {
        LetsEncryptOptions options = new() { Enabled = false };

        ValidateOptionsResult result = this._validator.Validate(null, options);

        Assert.That(result, Is.EqualTo(ValidateOptionsResult.Success));
    }

    /// <summary>
    /// Verifies required fields are enforced when enabled.
    /// </summary>
    [Test]
    public void Validate_WhenEnabled_RequiresCertDir()
    {
        LetsEncryptOptions options = new()
        {
            Enabled = true,
            AcmeAccountEmail = "admin@example.org",
            CloudflareApiToken = "token",
            CloudflareZoneId = "zone",
            DomainNames = ["news.example.org"],
        };

        ValidateOptionsResult result = this._validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("CertDir"));
    }
}
