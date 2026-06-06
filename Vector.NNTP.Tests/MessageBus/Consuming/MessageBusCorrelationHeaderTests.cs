// <copyright file="MessageBusCorrelationHeaderTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Reflection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Vector.NNTP.MessageBus.Consuming;
using Vector.NNTP.MessageBus.Publishing;

namespace Vector.NNTP.Tests.MessageBus.Consuming
{
    /// <summary>
    /// Verifies correlation header naming and consumer delivery wrapper extraction behavior.
    /// </summary>
    [TestFixture]
    internal sealed class MessageBusCorrelationHeaderTests
    {
        /// <summary>
        /// Ensures publisher and consumer code share one stable AMQP header name.
        /// </summary>
        [Test]
        public void CorrelationIdHeaderName_IsStableVectorHeader()
        {
            Assert.That(MessageBusCorrelationHeaders.CorrelationIdHeaderName, Is.EqualTo("x-vector-correlation-id"));
        }

        /// <summary>
        /// Ensures string correlation ids in AMQP headers are extracted for delivery logging.
        /// </summary>
        [Test]
        public void TryExtractCorrelationId_WhenStringHeaderPresent_ReturnsValue()
        {
            BasicDeliverEventArgs args = CreateDeliverArgs(new Dictionary<string, object?>
            {
                [MessageBusCorrelationHeaders.CorrelationIdHeaderName] = "renewal-abc-123",
            });

            string? correlationId = InvokeTryExtractCorrelationId(args);

            Assert.That(correlationId, Is.EqualTo("renewal-abc-123"));
        }

        /// <summary>
        /// Ensures UTF-8 byte correlation ids in AMQP headers are extracted.
        /// </summary>
        [Test]
        public void TryExtractCorrelationId_WhenByteHeaderPresent_ReturnsUtf8Value()
        {
            BasicDeliverEventArgs args = CreateDeliverArgs(new Dictionary<string, object?>
            {
                [MessageBusCorrelationHeaders.CorrelationIdHeaderName] = "scope-id"u8.ToArray(),
            });

            string? correlationId = InvokeTryExtractCorrelationId(args);

            Assert.That(correlationId, Is.EqualTo("scope-id"));
        }

        /// <summary>
        /// Ensures deliveries without correlation headers do not fabricate identifiers.
        /// </summary>
        [Test]
        public void TryExtractCorrelationId_WhenHeaderMissing_ReturnsNull()
        {
            BasicDeliverEventArgs args = CreateDeliverArgs(null);

            string? correlationId = InvokeTryExtractCorrelationId(args);

            Assert.That(correlationId, Is.Null);
        }

        /// <summary>
        /// Builds a <see cref="BasicDeliverEventArgs"/> instance for correlation extraction tests.
        /// </summary>
        /// <param name="headers">Optional AMQP headers dictionary.</param>
        /// <returns>Delivery arguments carrying the supplied headers.</returns>
        private static BasicDeliverEventArgs CreateDeliverArgs(IDictionary<string, object?>? headers)
        {
            BasicProperties properties = new();
            if (headers is not null)
            {
                properties.Headers = headers;
            }

            return new BasicDeliverEventArgs(
                "consumer-tag",
                42,
                false,
                "exchange",
                "routing-key",
                properties,
                ReadOnlyMemory<byte>.Empty,
                CancellationToken.None);
        }

        /// <summary>
        /// Invokes the consumer manager correlation extraction helper via reflection.
        /// </summary>
        /// <param name="args">Delivery arguments to inspect.</param>
        /// <returns>Extracted correlation id, if any.</returns>
        private static string? InvokeTryExtractCorrelationId(BasicDeliverEventArgs args)
        {
            MethodInfo? method = typeof(RabbitMqConsumerManager).GetMethod(
                "TryExtractCorrelationId",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string?)method!.Invoke(null, [args]);
        }
    }
}
