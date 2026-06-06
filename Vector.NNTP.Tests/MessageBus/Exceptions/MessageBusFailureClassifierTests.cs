// <copyright file="MessageBusFailureClassifierTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.MessageBus.Exceptions;

namespace Vector.NNTP.Tests.MessageBus.Exceptions
{
    /// <summary>
    /// Verifies bounded failure classification labels used by logs and metrics.
    /// </summary>
    [TestFixture]
    internal sealed class MessageBusFailureClassifierTests
    {
        /// <summary>
        /// Ensures lease timeout exceptions map to the lease_timeout label.
        /// </summary>
        [Test]
        public void Classify_LeaseTimeout_ReturnsLeaseTimeout()
        {
            Assert.That(
                MessageBusFailureClassifier.Classify(new MessageBusLeaseTimeoutException("test")),
                Is.EqualTo("lease_timeout"));
        }

        /// <summary>
        /// Ensures unavailable exceptions map to the unavailable label.
        /// </summary>
        [Test]
        public void Classify_Unavailable_ReturnsUnavailable()
        {
            Assert.That(
                MessageBusFailureClassifier.Classify(new MessageBusUnavailableException("test")),
                Is.EqualTo("unavailable"));
        }

        /// <summary>
        /// Ensures publish confirm timeout exceptions map to the confirm_timeout label.
        /// </summary>
        [Test]
        public void Classify_ConfirmTimeout_ReturnsConfirmTimeout()
        {
            Assert.That(
                MessageBusFailureClassifier.Classify(new MessageBusPublishConfirmTimeoutException("test")),
                Is.EqualTo("confirm_timeout"));
        }

        /// <summary>
        /// Ensures connection fault exceptions map to the connection_fault label.
        /// </summary>
        [Test]
        public void Classify_ConnectionFault_ReturnsConnectionFault()
        {
            Assert.That(
                MessageBusFailureClassifier.Classify(new MessageBusConnectionFaultException("test")),
                Is.EqualTo("connection_fault"));
        }

        /// <summary>
        /// Ensures cancellation is not classified as a generic fault.
        /// </summary>
        [Test]
        public void Classify_OperationCanceled_ReturnsCanceled()
        {
            Assert.That(MessageBusFailureClassifier.Classify(new OperationCanceledException()), Is.EqualTo("canceled"));
        }

        /// <summary>
        /// Ensures timeout exceptions receive a dedicated label.
        /// </summary>
        [Test]
        public void Classify_TimeoutException_ReturnsTimeout()
        {
            Assert.That(MessageBusFailureClassifier.Classify(new TimeoutException()), Is.EqualTo("timeout"));
        }

        /// <summary>
        /// Ensures unknown exceptions fall back to a single unexpected bucket.
        /// </summary>
        [Test]
        public void Classify_UnknownException_ReturnsUnexpected()
        {
            Assert.That(MessageBusFailureClassifier.Classify(new InvalidOperationException()), Is.EqualTo("unexpected"));
        }
    }
}
