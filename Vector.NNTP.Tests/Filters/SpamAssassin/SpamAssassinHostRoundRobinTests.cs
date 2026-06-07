// <copyright file="SpamAssassinHostRoundRobinTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// SpamAssassinHostRoundRobinTests.cs -- Multi-host round-robin and connect failover tests.

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Options;
using Vector.NNTP.Filters.SpamAssassin;

namespace Vector.NNTP.Tests.Filters.SpamAssassinTests;

/// <summary>
/// Tests for multi-host round-robin selection and connect failover in <see cref="SpamAssassin"/>.
/// </summary>
[TestFixture]
public sealed class SpamAssassinHostRoundRobinTests
{
    /// <summary>
    /// Verifies <see cref="SpamAssassinOptionsValidator"/> copies legacy <see cref="SpamAssassinOptions.Host"/> into <see cref="SpamAssassinOptions.Hosts"/>.
    /// </summary>
    [Test]
    public void Validator_NormalizesLegacyHostIntoHosts()
    {
        var options = new SpamAssassinOptions { Host = "198.18.0.70" };
        var validator = new SpamAssassinOptionsValidator();

        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(options.Hosts, Is.EqualTo(new[] { "198.18.0.70" }));
    }

    /// <summary>
    /// Verifies round-robin host attempt order rotates the first connect target across configured hosts.
    /// </summary>
    [Test]
    public void GetHostAttemptOrder_RoundRobin_RotatesFirstHost()
    {
        var options = new SpamAssassinOptions
        {
            Hosts = ["198.18.0.70", "198.18.0.71", "198.18.0.72"],
        };
        var client = new SpamAssassin(options);
        MethodInfo? method = typeof(SpamAssassin).GetMethod("GetHostAttemptOrder", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        string[] first = (string[])method!.Invoke(client, null)!;
        string[] second = (string[])method!.Invoke(client, null)!;
        string[] third = (string[])method!.Invoke(client, null)!;
        string[] fourth = (string[])method!.Invoke(client, null)!;

        Assert.That(first, Is.EqualTo(new[] { "198.18.0.70", "198.18.0.71", "198.18.0.72" }));
        Assert.That(second, Is.EqualTo(new[] { "198.18.0.71", "198.18.0.72", "198.18.0.70" }));
        Assert.That(third, Is.EqualTo(new[] { "198.18.0.72", "198.18.0.70", "198.18.0.71" }));
        Assert.That(fourth, Is.EqualTo(new[] { "198.18.0.70", "198.18.0.71", "198.18.0.72" }));
    }

    /// <summary>
    /// Verifies connect failover reaches a healthy host when the round-robin primary is unreachable.
    /// </summary>
    /// <returns>A task that completes when the assertion finishes.</returns>
    [Test]
    public async Task CheckAsync_FirstHostUnreachable_FailoverToSecondHost()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource listenCts = new();

        Task server = Task.Run(
            async () =>
            {
                while (!listenCts.IsCancellationRequested)
                {
                    TcpClient tcp = await listener.AcceptTcpClientAsync(listenCts.Token).ConfigureAwait(false);
                    using (tcp)
                    {
                        NetworkStream stream = tcp.GetStream();
                        byte[] buffer = new byte[4096];
                        while (true)
                        {
                            int read = await stream.ReadAsync(buffer, listenCts.Token).ConfigureAwait(false);
                            if (read == 0)
                            {
                                break;
                            }
                        }

                        byte[] response = Encoding.ASCII.GetBytes("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 1.0 / 5.0\r\n\r\n");
                        await stream.WriteAsync(response, listenCts.Token).ConfigureAwait(false);
                    }
                }
            },
            listenCts.Token);

        await Task.Delay(50).ConfigureAwait(false);

        var client = new SpamAssassin(
            new SpamAssassinOptions
            {
                Hosts = ["10.255.255.1", "127.0.0.1"],
                Port = port,
                ConnectTimeoutMilliseconds = 500,
                OperationTimeoutMilliseconds = 10_000,
            });

        SpamdCheckResult result = await client.CheckAsync(Encoding.UTF8.GetBytes("From: a@b\r\n\r\n\r\n")).ConfigureAwait(false);
        Assert.That(result.IsSpam, Is.False);
        Assert.That(result.Score, Is.EqualTo(1.0).Within(0.001));

        await listenCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await server.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Verifies <see cref="SpamAssassin.TellAsync"/> rejects unknown <c>Message-class</c> values before opening a connection.
    /// </summary>
    /// <returns>A task that completes when the assertion finishes.</returns>
    [Test]
    public async Task TellAsync_InvalidMessageClass_ThrowsArgumentException()
    {
        var client = new SpamAssassin(
            new SpamAssassinOptions
            {
                Host = "127.0.0.1",
            });

        ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(
            () => client.TellAsync(ReadOnlyMemory<byte>.Empty, "banana", null, null))!;

        Assert.That(ex.ParamName, Is.EqualTo("messageClass"));
    }
}
