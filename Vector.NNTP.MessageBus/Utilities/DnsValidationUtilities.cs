// DnsValidationUtilities.cs -- DNS resolution helpers for RabbitMQ host validation at startup.
//
// Validates that configured broker hostnames resolve before the pool opens TCP connections, surfacing mis-typed DNS
// names as validation errors instead of opaque connection timeouts.
//
// Thread safety:
//   Static methods; Dns.GetHostEntry may block — call only from ValidateOnStart paths, not hot paths.
//
// Cross-platform:
//   System.Net.Dns on Windows x64 and Linux x64.

using System.Net;

namespace Vector.NNTP.MessageBus.Utilities
{
    /// <summary>
    /// DNS resolution helpers used by <see cref="Configuration.RabbitMQOptions"/> cross-property validation.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Fail fast during <see cref="Configuration.RabbitMQOptionsValidator"/> when a hostname cannot
    /// resolve, instead of deferring failure to <see cref="RabbitMqConnectionFactory"/> connection attempts.</para>
    ///
    /// <para><b>IP literals:</b> <see cref="IPAddress.TryParse"/> succeeds without DNS lookup.</para>
    ///
    /// <para><b>Failure reporting:</b> DNS exceptions are captured into <paramref name="error"/> text for
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationResult"/> — not rethrown.</para>
    /// </remarks>
    internal static class DnsValidationUtilities
    {
        /// <summary>
        /// Validates that <paramref name="host"/> is an IP literal or resolves to at least one address.
        /// </summary>
        /// <param name="host">Bare hostname or IP literal (no port suffix).</param>
        /// <param name="error">Human-readable error when validation fails; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the host is acceptable for pool configuration.</returns>
        /// <remarks>
        /// <para><b>Blocking:</b> Uses <see cref="Dns.GetHostEntry(string)"/> for non-literal hosts — invoke only from startup
        /// validation, not from session hot paths.</para>
        /// </remarks>
        internal static bool ValidateHost(string host, out string? error)
        {
            if (IPAddress.TryParse(host, out _))
            {
                error = null;
                return true;
            }
            try
            {
                IPHostEntry entry = Dns.GetHostEntry(host);
                if (entry.AddressList.Length == 0)
                {
                    error = $"Host '{host}' did not resolve to any IP addresses.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Host '{host}' DNS resolution failed: {ex.Message}";
                return false;
            }
        }
    }
}

