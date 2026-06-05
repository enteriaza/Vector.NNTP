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

    /// <summary>
    /// Verifies placeholder Cloudflare API tokens are rejected when enabled.
    /// </summary>
    [Test]
    public void Validate_WhenEnabled_PlaceholderCloudflareApiToken_Fails()
    {
        LetsEncryptOptions options = CreateValidEnabledOptions();
        options.CloudflareApiToken = "changeme";

        ValidateOptionsResult result = this._validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("CloudflareApiToken"));
        Assert.That(result.FailureMessage, Does.Contain("placeholder"));
    }

    /// <summary>
    /// Verifies cluster signing secrets must not be placeholders when cluster sync is enabled.
    /// </summary>
    [Test]
    public void Validate_WhenClusterEnabled_PlaceholderSigningSecret_Fails()
    {
        LetsEncryptOptions options = CreateValidEnabledOptions();
        options.ClusterEnabled = true;
        options.ClusterBroadcastSigningSecret = "changeme";

        ValidateOptionsResult result = this._validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("ClusterBroadcastSigningSecret"));
        Assert.That(result.FailureMessage, Does.Contain("placeholder"));
    }

    /// <summary>
    /// Builds a minimally valid enabled options instance for negative tests.
    /// </summary>
    /// <returns>Enabled options with required fields populated.</returns>
    private static LetsEncryptOptions CreateValidEnabledOptions()
    {
        return new LetsEncryptOptions
        {
            Enabled = true,
            CertDir = @"C:\certs",
            AcmeAccountEmail = "admin@example.org",
            CloudflareApiToken = "cf-token-value",
            CloudflareZoneId = "zone-id",
            DomainNames = ["news.example.org"],
            AccountKeyPem = "-----BEGIN EC PRIVATE KEY-----\nMFcCAQEEI\n-----END EC PRIVATE KEY-----",
        };
    }
}
