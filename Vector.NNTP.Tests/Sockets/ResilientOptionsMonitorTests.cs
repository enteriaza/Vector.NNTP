// <copyright file="ResilientOptionsMonitorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Tests for <see cref="ResilientOptionsMonitor{TOptions}"/> last-known-good reload behavior.
/// </summary>
[TestFixture]
public sealed class ResilientOptionsMonitorTests
{
    /// <summary>
    /// Verifies the first failed read rethrows when no prior snapshot exists (startup path).
    /// </summary>
    [Test]
    public void Get_WhenInnerNeverSucceeded_ThrowsOptionsValidationException()
    {
        OptionsValidationException validationException = new("NntpServerOptions", typeof(NntpServerOptions), ["NodeName is required."]);
        var inner = new AlwaysThrowingOptionsMonitor(validationException);
        var subject = new ResilientOptionsMonitor<NntpServerOptions>(inner, NullLogger<ResilientOptionsMonitor<NntpServerOptions>>.Instance);

        OptionsValidationException thrown = Assert.Throws<OptionsValidationException>(() => _ = subject.Get(Options.DefaultName))!;
        Assert.That(thrown.Failures, Is.EquivalentTo(validationException.Failures));
    }

    /// <summary>
    /// Verifies a failed reload returns the last-known-good snapshot without throwing.
    /// </summary>
    [Test]
    public void Get_WhenInnerFailsAfterSuccess_ReturnsLastKnownGood()
    {
        NntpServerOptions good = CreateValidOptions();
        OptionsValidationException validationException = new(
            "NntpServerOptions",
            typeof(NntpServerOptions),
            ["Duplicate peer Name 'Giganews'."]);
        var inner = new ThrowingAfterFirstGetOptionsMonitor(good, validationException);
        var subject = new ResilientOptionsMonitor<NntpServerOptions>(inner, NullLogger<ResilientOptionsMonitor<NntpServerOptions>>.Instance);

        NntpServerOptions first = subject.Get(Options.DefaultName);
        NntpServerOptions second = subject.Get(Options.DefaultName);

        Assert.That(first, Is.SameAs(good));
        Assert.That(second, Is.SameAs(good));
    }

    /// <summary>
    /// Verifies accepted inner reload notifications propagate to resilient monitor listeners.
    /// </summary>
    [Test]
    public void OnChange_WhenInnerAcceptsReload_NotifiesListener()
    {
        NntpServerOptions initial = CreateValidOptions();
        NntpServerOptions updated = CreateValidOptions();
        updated.MaxConnections = 2048;
        var inner = new PublishableOptionsMonitor(initial);
        var subject = new ResilientOptionsMonitor<NntpServerOptions>(inner, NullLogger<ResilientOptionsMonitor<NntpServerOptions>>.Instance);
        NntpServerOptions? observed = null;
        using IDisposable subscription = subject.OnChange((options, _) => observed = options);

        inner.Publish(updated);

        Assert.That(observed, Is.SameAs(updated));
    }

    /// <summary>
    /// Verifies failed reload does not notify resilient monitor listeners.
    /// </summary>
    [Test]
    public void Get_WhenInnerFailsAfterSuccess_DoesNotNotifyListener()
    {
        NntpServerOptions good = CreateValidOptions();
        OptionsValidationException validationException = new(
            "NntpServerOptions",
            typeof(NntpServerOptions),
            ["Duplicate peer Name 'Giganews'."]);
        var inner = new ThrowingAfterFirstGetOptionsMonitor(good, validationException);
        var subject = new ResilientOptionsMonitor<NntpServerOptions>(inner, NullLogger<ResilientOptionsMonitor<NntpServerOptions>>.Instance);
        bool notified = false;
        using IDisposable subscription = subject.OnChange((_, _) => notified = true);

        _ = subject.Get(Options.DefaultName);
        _ = subject.Get(Options.DefaultName);

        Assert.That(notified, Is.False);
    }

    /// <summary>
    /// Creates a minimal valid <see cref="NntpServerOptions"/> instance.
    /// </summary>
    /// <returns>Valid server options.</returns>
    private static NntpServerOptions CreateValidOptions()
    {
        return new NntpServerOptions
        {
            NodeName = "test-node",
            ServerIdentification = "test-server",
        };
    }

    /// <summary>
    /// Inner monitor that always throws validation failures.
    /// </summary>
    private sealed class AlwaysThrowingOptionsMonitor : IOptionsMonitor<NntpServerOptions>
    {
        private readonly OptionsValidationException _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlwaysThrowingOptionsMonitor"/> class.
        /// </summary>
        /// <param name="exception">Exception to throw on every read.</param>
        internal AlwaysThrowingOptionsMonitor(OptionsValidationException exception)
        {
            _exception = exception;
        }

        /// <inheritdoc />
        public NntpServerOptions CurrentValue => Get(Options.DefaultName);

        /// <inheritdoc />
        public NntpServerOptions Get(string? name) => throw _exception;

        /// <inheritdoc />
        public IDisposable OnChange(Action<NntpServerOptions, string?> listener) => new NoopDisposable();
    }

    /// <summary>
    /// Inner monitor that succeeds once then throws on subsequent reads.
    /// </summary>
    private sealed class ThrowingAfterFirstGetOptionsMonitor : IOptionsMonitor<NntpServerOptions>
    {
        private readonly NntpServerOptions _good;
        private readonly OptionsValidationException _exception;
        private int _getCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingAfterFirstGetOptionsMonitor"/> class.
        /// </summary>
        /// <param name="good">First successful snapshot.</param>
        /// <param name="exception">Exception thrown on subsequent reads.</param>
        internal ThrowingAfterFirstGetOptionsMonitor(NntpServerOptions good, OptionsValidationException exception)
        {
            _good = good;
            _exception = exception;
        }

        /// <inheritdoc />
        public NntpServerOptions CurrentValue => Get(Options.DefaultName);

        /// <inheritdoc />
        public NntpServerOptions Get(string? name)
        {
            return Interlocked.Increment(ref _getCount) == 1
                ? _good
                : throw _exception;
        }

        /// <inheritdoc />
        public IDisposable OnChange(Action<NntpServerOptions, string?> listener) => new NoopDisposable();
    }

    /// <summary>
    /// Inner monitor that can publish accepted reload notifications to subscribers.
    /// </summary>
    private sealed class PublishableOptionsMonitor : IOptionsMonitor<NntpServerOptions>
    {
        private readonly List<Action<NntpServerOptions, string?>> _listeners = new();
        private NntpServerOptions _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishableOptionsMonitor"/> class.
        /// </summary>
        /// <param name="initial">Initial options snapshot.</param>
        internal PublishableOptionsMonitor(NntpServerOptions initial)
        {
            _value = initial;
        }

        /// <inheritdoc />
        public NntpServerOptions CurrentValue => Get(Options.DefaultName);

        /// <inheritdoc />
        public NntpServerOptions Get(string? name) => _value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<NntpServerOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new NoopDisposable();
        }

        /// <summary>
        /// Publishes a new accepted snapshot to all inner listeners.
        /// </summary>
        /// <param name="next">Accepted options snapshot.</param>
        internal void Publish(NntpServerOptions next)
        {
            _value = next;
            foreach (Action<NntpServerOptions, string?> listener in _listeners.ToArray())
            {
                listener(next, Options.DefaultName);
            }
        }
    }

    /// <summary>
    /// No-op disposable for test monitors.
    /// </summary>
    private sealed class NoopDisposable : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
