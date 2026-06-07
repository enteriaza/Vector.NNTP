// <copyright file="MessageBusCorrelationHeaders.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusCorrelationHeaders.cs -- Shared AMQP header names used for trace and correlation propagation.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Declares the stable AMQP header names used for MessageBus correlation metadata.
    /// </summary>
    /// <remarks>
    /// <para><b>Policy:</b> Header names are centralized to prevent divergent literals between publisher and consumer code paths.</para>
    /// </remarks>
    internal static class MessageBusCorrelationHeaders
    {
        /// <summary>
        /// Header carrying a caller-provided correlation identifier.
        /// </summary>
        internal const string CorrelationIdHeaderName = "x-vector-correlation-id";
    }
}
