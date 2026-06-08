// <copyright file="ResilientOptionsMonitor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: IOptionsMonitor wrapper that retains last-known-good values when reload validation fails.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// <see cref="IOptionsMonitor{TOptions}"/> decorator that retains the last successfully validated snapshot when a
    /// configuration reload fails validation.
    /// </summary>
    /// <typeparam name="TOptions">Bound options type.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Enables hot reload of host JSON (for example <c>NNTPD.json</c> with <c>reloadOnChange: true</c>)
    /// without tearing down live NNTP sessions when an operator saves an invalid edit. Startup behavior is unchanged:
    /// the first successful read must succeed or <see cref="Get"/> rethrows so <c>ValidateOnStart</c> can fail the host.
    /// </para>
    /// <para>
    /// <b>Production use:</b> Registered as <see cref="IOptionsMonitor{NntpServerOptions}"/> from
    /// <see cref="Hosting.ServiceCollectionExtensions.AddNntpSocketsCore"/>.
    /// </para>
    /// <para><b>Thread safety:</b> Safe for concurrent <see cref="Get"/> from session and sampler threads.</para>
    /// </remarks>
    internal sealed partial class ResilientOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>, IDisposable
        where TOptions : class
    {
        /// <summary>
        /// Minimum elapsed time before an identical validation failure signature is logged again.
        /// </summary>
        private static readonly long FailureLogDedupWindowTicks = TimeSpan.FromSeconds(30).Ticks;

        /// <summary>
        /// Inner monitor performing bind and validation via the standard options pipeline.
        /// </summary>
        private readonly IOptionsMonitor<TOptions> _inner;

        /// <summary>
        /// Category logger for reload validation failures.
        /// </summary>
        private readonly ILogger<ResilientOptionsMonitor<TOptions>> _logger;

        /// <summary>
        /// Synchronizes listener registration and notification snapshots.
        /// </summary>
        private readonly object _listenerLock = new();

        /// <summary>
        /// Registered change listeners notified only after successful reload acceptance.
        /// </summary>
        private readonly List<ChangeListenerRegistration> _listeners = [];

        /// <summary>
        /// Last options instance returned successfully from the inner monitor.
        /// </summary>
        private TOptions? _lastKnownGood;

        /// <summary>
        /// Pipe-delimited validation failure text from the most recent reload rejection log.
        /// </summary>
        private string? _lastLoggedFailureSignature;

        /// <summary>
        /// <see cref="Environment.TickCount64"/> when the last reload rejection was logged.
        /// </summary>
        private long _lastLoggedFailureTickCount;

        /// <summary>
        /// Creates a resilient monitor over the standard <see cref="OptionsMonitor{TOptions}"/> pipeline.
        /// </summary>
        /// <param name="factory">Options factory from DI.</param>
        /// <param name="changeTokenSources">Configuration change token sources from DI.</param>
        /// <param name="cache">Options monitor cache from DI.</param>
        /// <param name="logger">Logger for reload validation failures.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when any dependency is <see langword="null"/>.
        /// </exception>
        public ResilientOptionsMonitor(
            IOptionsFactory<TOptions> factory,
            IEnumerable<IOptionsChangeTokenSource<TOptions>> changeTokenSources,
            IOptionsMonitorCache<TOptions> cache,
            ILogger<ResilientOptionsMonitor<TOptions>> logger)
            : this(new OptionsMonitor<TOptions>(factory, changeTokenSources, cache), logger)
        {
        }

        /// <summary>
        /// Creates a resilient monitor over a supplied inner monitor (unit tests).
        /// </summary>
        /// <param name="inner">Inner monitor to wrap. Must not be <see langword="null"/>.</param>
        /// <param name="logger">Logger for reload validation failures.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inner"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        internal ResilientOptionsMonitor(
            IOptionsMonitor<TOptions> inner,
            ILogger<ResilientOptionsMonitor<TOptions>> logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = _inner.OnChange(OnInnerOptionsChanged);
        }

        /// <summary>
        /// Gets the current options snapshot, retaining the previous snapshot when reload validation fails.
        /// </summary>
        /// <value>Result of <see cref="Get"/> for <see cref="Options.DefaultName"/>.</value>
        public TOptions CurrentValue => Get(Options.DefaultName);

        /// <summary>
        /// Returns the named options snapshot, retaining the previous snapshot when reload validation fails.
        /// </summary>
        /// <param name="name">Options name (typically <see cref="Options.DefaultName"/>).</param>
        /// <returns>The successfully validated options instance.</returns>
        /// <exception cref="OptionsValidationException">
        /// Thrown when the inner monitor fails validation and no prior successful snapshot exists (startup path).
        /// </exception>
        public TOptions Get(string? name)
        {
            name ??= Options.DefaultName;
            try
            {
                TOptions value = _inner.Get(name);
                Volatile.Write(ref _lastKnownGood, value);
                return value;
            }
            catch (OptionsValidationException ex)
            {
                TOptions? cached = Volatile.Read(ref _lastKnownGood);
                if (cached is null)
                {
                    throw;
                }

                LogValidationFailureRetainedPrevious(ex);
                return cached;
            }
        }

        /// <summary>
        /// Registers a listener invoked only when a reload is accepted by the inner monitor.
        /// </summary>
        /// <param name="listener">Callback receiving the new options and options name.</param>
        /// <returns>Token that removes the listener when disposed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="listener"/> is <see langword="null"/>.</exception>
        public IDisposable OnChange(Action<TOptions, string?> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            ChangeListenerRegistration registration = new(listener);
            lock (_listenerLock)
            {
                _listeners.Add(registration);
            }

            return new UnregisterToken(this, registration);
        }

        /// <summary>
        /// Disposes the inner monitor when it implements <see cref="IDisposable"/>.
        /// </summary>
        public void Dispose()
        {
            if (_inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Stores a successful reload from the inner monitor and notifies registered listeners.
        /// </summary>
        /// <param name="options">Accepted options instance.</param>
        /// <param name="name">Options name supplied by the inner monitor.</param>
        private void OnInnerOptionsChanged(TOptions options, string? name)
        {
            name ??= Options.DefaultName;
            Volatile.Write(ref _lastKnownGood, options);
            NotifyListeners(options, name);
        }

        /// <summary>
        /// Invokes active listeners for an accepted reload.
        /// </summary>
        /// <param name="options">Accepted options instance.</param>
        /// <param name="name">Options name.</param>
        private void NotifyListeners(TOptions options, string name)
        {
            ChangeListenerRegistration[] snapshot;
            lock (_listenerLock)
            {
                snapshot = [.. _listeners];
            }

            foreach (ChangeListenerRegistration registration in snapshot)
            {
                if (!registration.IsActive)
                {
                    continue;
                }

                try
                {
                    registration.Listener(options, name);
                }
                catch (Exception ex)
                {
                    LogChangeListenerFault(_logger, ex);
                }
            }
        }

        /// <summary>
        /// Logs a reload validation failure once per distinct failure signature within the dedup window.
        /// </summary>
        /// <param name="ex">Validation exception from the inner monitor.</param>
        private void LogValidationFailureRetainedPrevious(OptionsValidationException ex)
        {
            string signature = string.Join('|', ex.Failures);
            long now = Environment.TickCount64;
            long last = Volatile.Read(ref _lastLoggedFailureTickCount);
            string? lastSignature = Volatile.Read(ref _lastLoggedFailureSignature);
            if (string.Equals(lastSignature, signature, StringComparison.Ordinal) &&
                now - last < FailureLogDedupWindowTicks)
            {
                return;
            }

            Volatile.Write(ref _lastLoggedFailureSignature, signature);
            Volatile.Write(ref _lastLoggedFailureTickCount, now);
            LogOptionsValidationFailedRetainedPrevious(_logger, signature);
        }

        /// <summary>
        /// Removes a disposed listener registration.
        /// </summary>
        /// <param name="registration">Registration to deactivate and remove.</param>
        private void RemoveListener(ChangeListenerRegistration registration)
        {
            lock (_listenerLock)
            {
                registration.IsActive = false;
                _ = _listeners.Remove(registration);
            }
        }

        /// <summary>
        /// Holds a change listener callback and active flag.
        /// </summary>
        private sealed class ChangeListenerRegistration
        {
            /// <summary>
            /// Initializes a new listener registration.
            /// </summary>
            /// <param name="listener">Callback to invoke on accepted reloads.</param>
            internal ChangeListenerRegistration(Action<TOptions, string?> listener)
            {
                Listener = listener;
            }

            /// <summary>Gets the listener callback.</summary>
            internal Action<TOptions, string?> Listener { get; }

            /// <summary>Gets or sets whether the registration is still active.</summary>
            internal bool IsActive { get; set; } = true;
        }

        /// <summary>
        /// Disposes a listener registration on the resilient monitor.
        /// </summary>
        private sealed class UnregisterToken : IDisposable
        {
            /// <summary>Parent monitor, cleared after dispose.</summary>
            private ResilientOptionsMonitor<TOptions>? _owner;

            /// <summary>Registration to remove, cleared after dispose.</summary>
            private ChangeListenerRegistration? _registration;

            /// <summary>
            /// Initializes a new unregister token.
            /// </summary>
            /// <param name="owner">Parent monitor.</param>
            /// <param name="registration">Registration to remove on dispose.</param>
            internal UnregisterToken(ResilientOptionsMonitor<TOptions> owner, ChangeListenerRegistration registration)
            {
                _owner = owner;
                _registration = registration;
            }

            /// <summary>Removes the listener from the parent monitor.</summary>
            public void Dispose()
            {
                if (_owner is not null && _registration is not null)
                {
                    _owner.RemoveListener(_registration);
                }

                _owner = null;
                _registration = null;
            }
        }
    }
}
