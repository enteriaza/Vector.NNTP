// EnvironmentUtilities.cs — Safe hostname resolution with deterministic fallback for containerized environments.
//
// Centralizes the Environment.MachineName access pattern to handle edge cases where the OS hostname
// is empty (misconfigured containers) or throws InvalidOperationException (embedded Linux systems).
// Previously, each call site independently handled these cases with inconsistent fallback values.
//
// All values are determined at runtime via exception-safe access to Environment.MachineName.
//
// Thread safety:
//   All methods are static and stateless.  Environment.MachineName is thread-safe per BCL documentation.
//   Safe for concurrent invocation from any number of threads without synchronization.
//
// Cross-platform:
//   Fully portable.  Environment.MachineName uses gethostname(2) on Linux and GetComputerName on Windows.
//   Both are BCL-abstracted and available on all .NET 8 runtimes (Windows x64, Linux x64).
//   No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Not applicable.  Single string property access with fallback logic.

namespace Vector.NNTP.MessageBus.Utilities
{
    /// <summary>
    /// Safe environment inspection helpers with deterministic fallbacks for containerized and embedded environments.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Provides a single, tested implementation of the safe <see cref="Environment.MachineName"/>
    /// access pattern. <see cref="Environment.MachineName"/> can return an empty string on misconfigured containers
    /// (blank <c>/etc/hostname</c>, missing <c>--hostname</c> Docker flag) and can throw
    /// <see cref="InvalidOperationException"/> on some embedded Linux kernels with misconfigured
    /// <c>/proc/sys/kernel/hostname</c>. Both edge cases are handled here so callers receive a guaranteed non-null,
    /// non-empty hostname.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.
    /// <see cref="Environment.MachineName"/> is thread-safe per BCL documentation.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable. <see cref="Environment.MachineName"/> uses <c>gethostname(2)</c>
    /// on Linux and <c>GetComputerName</c> on Windows -- both are BCL-abstracted and available on all .NET 8 runtimes
    /// (Windows x64, Linux x64). No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable. Single string property access with a fallback.</para>
    /// </remarks>
    internal static class EnvironmentUtilities
    {

        #region Constants

        /// <summary>
        /// Fallback hostname used when <see cref="Environment.MachineName"/> returns an empty or whitespace-only
        /// string, or when the property accessor throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> <c>"unknown-host"</c>. Chosen to be clearly identifiable in logs and queue names
        /// so operators can diagnose the misconfigured hostname immediately.</para>
        /// <para><b>RFC 952 compliance:</b> Contains only lowercase ASCII letters and hyphens -- valid as both a DNS
        /// hostname label and a RabbitMQ queue name component.</para>
        /// </remarks>
        internal const string FallbackHostname = "unknown-host";

        #endregion

        #region Public Methods

        /// <summary>
        /// Resolves the machine hostname from <see cref="Environment.MachineName"/> with a deterministic fallback to
        /// <see cref="FallbackHostname"/>.
        /// </summary>
        /// <param name="usedFallback">Set to <see langword="true"/> if <see cref="FallbackHostname"/> was used
        /// because <see cref="Environment.MachineName"/> returned empty/whitespace or threw an exception.</param>
        /// <returns>A non-null, non-empty hostname string. Guaranteed to be either the OS-reported hostname or
        /// <see cref="FallbackHostname"/>.</returns>
        /// <remarks>
        /// <para><b>Exception handling:</b> <see cref="Environment.MachineName"/> can throw
        /// <see cref="InvalidOperationException"/> on some embedded Linux configurations where
        /// <c>/proc/sys/kernel/hostname</c> is inaccessible or malformed. This is extremely rare but must not
        /// prevent caller operation. The exception is caught and the fallback value is returned.</para>
        ///
        /// <para><b>Empty/whitespace guard:</b> Misconfigured containers can yield an empty hostname (blank
        /// <c>/etc/hostname</c>, missing <c>--hostname</c> Docker flag). <see cref="string.IsNullOrWhiteSpace"/>
        /// catches both empty strings and whitespace-only strings.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Never thrown; included for documentation completeness per BCL
        /// patterns.</exception>
        public static string ResolveMachineName(out bool usedFallback)
        {
            string? name = GetSystemHostname();
            if (!string.IsNullOrWhiteSpace(name))
            {
                usedFallback = false;
                return name;
            }
            usedFallback = true;
            return FallbackHostname;
        }

        /// <summary>
        /// Resolves the machine hostname from <see cref="Environment.MachineName"/> with a deterministic fallback to
        /// <see cref="FallbackHostname"/>.
        /// </summary>
        /// <returns>A non-null, non-empty hostname string. Guaranteed to be either the OS-reported hostname or
        /// <see cref="FallbackHostname"/>.</returns>
        /// <remarks>
        /// Convenience overload that discards the <c>usedFallback</c> indicator. Use
        /// <see cref="ResolveMachineName(out bool)"/> when the caller needs to log a warning about fallback usage.
        /// </remarks>
        public static string ResolveMachineName()
        {
            return ResolveMachineName(out _);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Attempts to retrieve the system hostname from <see cref="Environment.MachineName"/> with exception safety.
        /// </summary>
        /// <returns>The OS-reported hostname, or <see langword="null"/> if the property access failed.</returns>
        /// <remarks>
        /// <para><b>Exception safety:</b> <see cref="Environment.MachineName"/> can throw
        /// <see cref="InvalidOperationException"/> on embedded Linux systems with misconfigured hostname access.
        /// This method swallows the exception and returns <see langword="null"/> to allow the caller to apply the
        /// fallback value.</para>
        /// </remarks>
        private static string? GetSystemHostname()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (InvalidOperationException)
            {
                // Extremely rare -- embedded Linux kernels with misconfigured /proc/sys/kernel/hostname.
                return null;
            }
        }

        #endregion

    }
}
