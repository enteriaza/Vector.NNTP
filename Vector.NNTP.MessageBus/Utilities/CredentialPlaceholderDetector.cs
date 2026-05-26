// CredentialPlaceholderDetector.cs -- Detects template placeholder credentials in configuration strings.
//
// Used by RabbitMQOptions validation to reject changeme-style secrets in production-bound configuration without
// embedding section-specific placeholder lists in every validator call site.
//
// Thread safety:
//   All members are static; CommonPlaceholders is immutable after type initialization.
//
// Cross-platform:
//   Fully portable; FrozenSet lookups on .NET 8.

using System.Collections.Frozen;

namespace Vector.NNTP.MessageBus.Utilities
{
    /// <summary>
    /// Detects template placeholder credentials in configuration strings before hosts connect to RabbitMQ.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Centralises common placeholder tokens so <see cref="Configuration.RabbitMQOptions"/> and other
    /// sections share one frozen set with optional per-section extensions.</para>
    ///
    /// <para><b>Comparison:</b> Uses <see cref="StringComparer.OrdinalIgnoreCase"/> for placeholder matching; whitespace-only
    /// values are treated as placeholders.</para>
    ///
    /// <para><b>Allocation:</b> <see cref="CommonPlaceholders"/> is built once at type load; <see cref="IsPlaceholder"/> performs
    /// O(1) frozen set lookups without per-call allocations.</para>
    /// </remarks>
    internal static class CredentialPlaceholderDetector
    {
        /// <summary>
        /// Shared placeholder tokens rejected during options validation.
        /// </summary>
        /// <remarks>Immutable after static initialization; safe for concurrent reads.</remarks>
        internal static readonly FrozenSet<string> CommonPlaceholders = FrozenSet.ToFrozenSet(
            ["changeme", "password", "your-password-here", "replace-me", "todo", "fixme"],
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="value"/> is empty, whitespace, or matches a known placeholder.
        /// </summary>
        /// <param name="value">Credential string from configuration binding.</param>
        /// <param name="additionalPlaceholders">Optional section-specific placeholders merged with <see cref="CommonPlaceholders"/>.</param>
        /// <returns><see langword="true"/> when the value must be rejected as a template placeholder.</returns>
        /// <remarks>
        /// <para><b>Order:</b> Checks null/whitespace first, then <see cref="CommonPlaceholders"/>, then
        /// <paramref name="additionalPlaceholders"/> when supplied.</para>
        /// </remarks>
        internal static bool IsPlaceholder(string? value, FrozenSet<string>? additionalPlaceholders = null)
        {
            return string.IsNullOrWhiteSpace(value)
                || CommonPlaceholders.Contains(value)
                || additionalPlaceholders?.Contains(value) == true;
        }
    }
}

