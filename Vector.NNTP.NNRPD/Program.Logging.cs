// <copyright file="Program.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Program.Logging.cs -- Bootstrap logger configuration, global exception handlers, and startup banner.
//
// Lifecycle: ConfigureBootstrapLogger runs before the host is built; RegisterGlobalExceptionHandlers is registered
// outside try/catch; LogStartupBanner runs after ConfigureSerilog (Program.Serilog.cs).
//
// Threading: Exception handlers run on the triggering thread (finalizer thread for AppDomain.UnhandledException).

using System.Runtime.InteropServices;
using Vector.NNTP.MessageBus.Utilities;
using Serilog;

namespace Vector.NNTP.NNRPD
{
    /// <summary>
    /// Serilog bootstrap logging and process-wide exception handlers for the NNRPD host.
    /// </summary>
    public partial class Program
    {

        #region Bootstrap Logger

        /// <summary>
        /// Configures the Serilog bootstrap logger available before the host and DI container are built.
        /// </summary>
        /// <remarks>
        /// <para>Uses <c>CreateBootstrapLogger()</c> so the full Serilog pipeline registered in
        /// <see cref="ConfigureSerilog"/> can replace this logger without duplicating sinks.</para>
        /// </remarks>
        private static void ConfigureBootstrapLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateBootstrapLogger();
        }

        #endregion

        #region Global Exception Handlers

        /// <summary>
        /// Registers <see cref="AppDomain.UnhandledException"/> and <see cref="TaskScheduler.UnobservedTaskException"/>
        /// handlers so faults are never silent.
        /// </summary>
        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                    {
                        Log.Fatal(ex, "Unhandled exception on background thread (IsTerminating={IsTerminating})", e.IsTerminating);
                    }
                    else
                    {
                        Log.Fatal(
                            "Non-exception unhandled object: {ExceptionObject} (IsTerminating={IsTerminating})",
                            e.ExceptionObject,
                            e.IsTerminating);
                    }
                }
                finally
                {
                    try
                    {
                        Log.CloseAndFlush();
                    }
                    catch
                    {
                        // Process is terminating; swallow sink failures during terminal flush.
                    }
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved Task exception -- missing await or unobserved fire-and-forget Task");
                e.SetObserved();
            };
        }

        #endregion

        #region Startup Banner

        /// <summary>
        /// Logs deployment identity, GC configuration, and system resources at startup.
        /// </summary>
        private static void LogStartupBanner()
        {
            long totalAvailableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            Log.Information(
                "Starting: Version={Version}, OS={OS}, Runtime={Runtime}, Arch={Arch}, CPUs={CpuCount}, " +
                "TotalMemoryGiB={TotalMemoryGiB:F2}, ServerGC={ServerGC}, LatencyMode={LatencyMode}, PID={Pid}, Hostname={Hostname}",
                AssemblyInfoUtilities.ApplicationVersion,
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture,
                Environment.ProcessorCount,
                FormattingUtilities.ToGiB(totalAvailableBytes),
                System.Runtime.GCSettings.IsServerGC,
                System.Runtime.GCSettings.LatencyMode,
                Environment.ProcessId,
                EnvironmentUtilities.ResolveMachineName());

            if (!System.Runtime.GCSettings.IsServerGC)
            {
                Log.Warning(
                    "ServerGC is DISABLED -- workstation GC causes frequent Gen2 pauses that stall IOCP threads under load. " +
                    "Throughput and latency will be severely degraded with concurrent connections. " +
                    "Enable ServerGC in the .csproj (<ServerGarbageCollection>true</ServerGarbageCollection>) or " +
                    "runtimeconfig.json (\"System.GC.Server\": true)");
            }
        }

        #endregion
    }
}
