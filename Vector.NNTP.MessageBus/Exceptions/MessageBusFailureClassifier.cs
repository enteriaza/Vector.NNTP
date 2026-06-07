// <copyright file="MessageBusFailureClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusFailureClassifier.cs -- Maps exceptions to bounded failure classes for logs and metrics.

namespace Vector.NNTP.MessageBus.Exceptions
{
    /// <summary>
    /// Classifies MessageBus exceptions into stable low-cardinality categories.
    /// </summary>
    /// <remarks>
    /// <para><b>Policy:</b> Returned class labels are intended for structured logs and metrics tags.</para>
    /// <para><b>Cardinality:</b> All labels are ASCII, bounded, and suitable for long-running telemetry pipelines.</para>
    /// </remarks>
    internal static class MessageBusFailureClassifier
    {
        /// <summary>
        /// Classifies the supplied exception into a stable category name.
        /// </summary>
        /// <param name="exception">Exception to classify.</param>
        /// <returns>Bounded class string for logs and metrics.</returns>
        internal static string Classify(Exception exception)
        {
            return exception switch
            {
                OperationCanceledException => "canceled",
                MessageBusLeaseTimeoutException => "lease_timeout",
                MessageBusUnavailableException => "unavailable",
                MessageBusPublishConfirmTimeoutException => "confirm_timeout",
                MessageBusConnectionFaultException => "connection_fault",
                TimeoutException => "timeout",
                _ => "unexpected",
            };
        }
    }
}
