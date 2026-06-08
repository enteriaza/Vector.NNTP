// <copyright file="ArticlesSpoolTelemetryTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Vector.NNTP.Articles.Telemetry;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticlesSpoolTelemetry"/> activity source registration and span names.
/// </summary>
[TestFixture]
public sealed class ArticlesSpoolTelemetryTests
{
    /// <summary>
    /// Verifies spool operation names can be started and are no-ops when no listener is attached.
    /// </summary>
    [Test]
    public void StartActivity_WithoutListener_ReturnsNullWithoutThrowing()
    {
        using Activity? activity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
            ArticlesSpoolTelemetry.PreprocessOperation,
            ActivityKind.Internal);

        Assert.That(activity, Is.Null);
    }

    /// <summary>
    /// Verifies the activity source exposes the expected spool operation names when a listener is attached.
    /// </summary>
    [Test]
    public void StartActivity_WithListener_EmitsExpectedOperationNames()
    {
        var observed = new List<string>();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == ArticlesSpoolTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => observed.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        using (ArticlesSpoolTelemetry.ActivitySource.StartActivity(ArticlesSpoolTelemetry.PreprocessOperation))
        {
        }

        using (ArticlesSpoolTelemetry.ActivitySource.StartActivity(ArticlesSpoolTelemetry.WriteOperation, ActivityKind.Client))
        {
        }

        Assert.That(
            observed,
            Is.EqualTo(
            [
                ArticlesSpoolTelemetry.PreprocessOperation,
                ArticlesSpoolTelemetry.WriteOperation,
            ]));
    }
}
