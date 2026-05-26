// <copyright file="Program.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Program.cs -- NNRPD application entry and host composition root.
//
// Serilog bootstrap and global exception handlers: Program.Logging.cs.
// Serilog DI registration: Program.Serilog.cs.
// Host JSON and MessageBus: Program.Configuration.cs, Program.MessageBus.cs.

using Microsoft.Extensions.Options;
using Serilog;

namespace NNRPD
{
    /// <summary>
    /// Application entry point and composition root for the NNRPD worker host.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Main entry point for the NNRPD worker service.
        /// </summary>
        /// <param name="args">Command-line arguments forwarded to the host configuration.</param>
        /// <returns>A task that completes when the host shuts down.</returns>
        public static async Task Main(string[] args)
        {
            ConfigureBootstrapLogger();
            RegisterGlobalExceptionHandlers();

            try
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
                AddHostConfiguration(builder);
                ConfigureSerilog(builder);
                LogStartupBanner();
                ConfigureMessageBus(builder);
                _ = builder.Services.AddHostedService<Worker>();

                using IHost host = builder.Build();
                await host.RunAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown (Ctrl+C, SIGTERM, systemd stop).
            }
            catch (OptionsValidationException ex)
            {
                Log.Fatal("Configuration validation failed: {Failures}", string.Join("; ", ex.Failures));
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Terminated unexpectedly");
                Environment.ExitCode = 1;
            }
            finally
            {
                if (Environment.ExitCode != 0)
                {
                    Log.Warning("Shutdown with failure (ExitCode={ExitCode})", Environment.ExitCode);
                }
                else
                {
                    Log.Information("Shutdown complete (ExitCode={ExitCode})", Environment.ExitCode);
                }

                try
                {
                    await Log.CloseAndFlushAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best effort -- sink failure during shutdown cannot be recovered.
                }
            }
        }
    }
}
