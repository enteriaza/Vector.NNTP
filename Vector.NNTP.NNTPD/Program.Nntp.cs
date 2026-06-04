// <copyright file="Program.Nntp.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Auth.MySql;
using Vector.NNTP.HistoryDB.DependencyInjection;
using Vector.NNTP.Session.Redis.DependencyInjection;
using Vector.NNTP.Sockets.Hosting;
using SocketsNntpServerOptions = Vector.NNTP.Sockets.Configuration.NntpServerOptions;

namespace Vector.NNTP.NNTPD
{
    /// <summary>
    /// NNTP transit socket server (MODE STREAM) host configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shared <c>NntpServer</c> JSON section binds two option types: Encryption uses <c>NodeName</c>;
    /// Sockets uses <c>Port</c>, <c>TlsPort</c>, <c>BindAddress</c>, <c>EnableStartTls</c>,
    /// <c>ServerIdentification</c>, and related listener settings. See <c>Docs/nntp-host-configuration.md</c>.
    /// </para>
    /// </remarks>
    public partial class Program
    {
        /// <summary>
        /// Registers transit-profile NNTP sockets (cleartext, implicit TLS, STARTTLS).
        /// </summary>
        /// <param name="builder">Host builder.</param>
        private static void ConfigureNntpTransit(HostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.Services.AddNntpSessionRedis(builder.Configuration);
            _ = builder.Services.AddNntpHistoryDatabase(builder.Configuration);
            _ = builder.Services.AddNntpMySqlAuthFromHostConfiguration(builder.Configuration);
            _ = builder.Services.AddNntpSocketsTransit();

            _ = builder.Services.PostConfigure<SocketsNntpServerOptions>(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ServerIdentification))
                {
                    options.ServerIdentification = "Vector.NNTP.NNTPD";
                }
            });
        }
    }
}
