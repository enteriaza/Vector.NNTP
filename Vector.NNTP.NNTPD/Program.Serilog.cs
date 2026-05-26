// <copyright file="Program.Serilog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Program.Serilog.cs -- Serilog dependency injection for the NNTPD host.
//
// Replaces default Microsoft logging providers with Serilog as the primary ILogger implementation.

using Vector.NNTP.MessageBus.Utilities;
using Serilog;

namespace Vector.NNTP.NNTPD
{
    /// <summary>
    /// Serilog host registration for the NNTPD worker.
    /// </summary>
    public partial class Program
    {

        #region Constants

        /// <summary>Serilog rolling file name prefix (day suffix appended by the sink).</summary>
        private const string LogFileName = "NNTPD-.log";

        /// <summary>Default directory for rolling log files under the content root.</summary>
        private static readonly string DefaultLogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

        /// <summary>Number of rolled log files to retain.</summary>
        private const int RetainedFileCountLimit = 21;

        /// <summary>Interval between forced flushes of the file sink buffer.</summary>
        private static readonly TimeSpan FlushToDiskInterval = TimeSpan.FromSeconds(1);

#if DEBUG
        private const string ConsoleOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [P:{ProcessId} T:{ThreadId}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
#else
        private const string ConsoleOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [P:{ProcessId} T:{ThreadId}] [{Level:u3}]: {Message:lj}{NewLine}{Exception}";
#endif

#if DEBUG
        private const string FileOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [P:{ProcessId} T:{ThreadId}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
#else
        private const string FileOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [P:{ProcessId} T:{ThreadId}] [{Level:u3}]: {Message:lj}{NewLine}{Exception}";
#endif

        #endregion

        /// <summary>
        /// Configures Serilog and registers it with the host DI container.
        /// </summary>
        /// <param name="builder">Host application builder.</param>
        private static void ConfigureSerilog(HostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", AssemblyInfoUtilities.ApplicationName)
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .WriteTo.Async(asyncConfig => asyncConfig.Console(outputTemplate: ConsoleOutputTemplate))
                .WriteTo.Async(asyncConfig => asyncConfig.File(
                    path: Path.Combine(DefaultLogDirectory, LogFileName),
                    outputTemplate: FileOutputTemplate,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCountLimit,
                    fileSizeLimitBytes: null,
                    rollOnFileSizeLimit: false,
                    buffered: true,
                    flushToDiskInterval: FlushToDiskInterval))
                .CreateLogger();

            _ = builder.Logging.ClearProviders();

            _ = builder.Services.AddSerilog();
        }
    }
}
