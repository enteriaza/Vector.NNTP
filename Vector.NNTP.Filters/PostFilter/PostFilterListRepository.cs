// <copyright file="PostFilterListRepository.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterListRepository.cs -- Loads banlist, badwords, and whitelist text files when paths change.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Loads banlist, badwords, and whitelist text files from <see cref="PostFilterOptions.DataDirectory"/> when paths change.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> Snapshots are swapped under a lock; consumers read the current snapshot reference.</para>
    /// </remarks>
    public sealed partial class PostFilterListRepository : IDisposable
    {
        /// <summary>Logger for reload and load-failure events.</summary>
        private readonly ILogger<PostFilterListRepository> _logger;

        /// <summary><see cref="IOptionsMonitor{T}.OnChange"/> subscription; disposed with the repository.</summary>
        private readonly IDisposable? _subscription;

        /// <summary>Lock protecting swaps of <see cref="_banlist"/>, <see cref="_badwords"/>, and <see cref="_whitelist"/>.</summary>
        private readonly object _sync = new();

        /// <summary>Current banlist lines (lowercase substrings/tokens); replaced atomically on reload.</summary>
        private IReadOnlyList<string> _banlist = [];

        /// <summary>Current badword substrings; replaced atomically on reload.</summary>
        private IReadOnlyList<string> _badwords = [];

        /// <summary>Current whitelist patterns; replaced atomically on reload.</summary>
        private IReadOnlyList<string> _whitelist = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="PostFilterListRepository"/> class.
        /// </summary>
        /// <param name="options">Options monitor.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
        public PostFilterListRepository(IOptionsMonitor<PostFilterOptions> options, ILogger<PostFilterListRepository> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Reload(options.CurrentValue);
            _subscription = options.OnChange((o, _) => Reload(o));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _subscription?.Dispose();
        }

        /// <summary>
        /// Gets a snapshot of banlist entries (lowercase substrings / tokens).
        /// </summary>
        /// <returns>Banlist lines.</returns>
        public IReadOnlyList<string> BanlistSnapshot()
        {
            lock (_sync)
            {
                return _banlist;
            }
        }

        /// <summary>
        /// Gets a snapshot of badword substrings.
        /// </summary>
        /// <returns>Badword lines.</returns>
        public IReadOnlyList<string> BadwordsSnapshot()
        {
            lock (_sync)
            {
                return _badwords;
            }
        }

        /// <summary>
        /// Gets a snapshot of whitelist patterns.
        /// </summary>
        /// <returns>Whitelist lines.</returns>
        public IReadOnlyList<string> WhitelistSnapshot()
        {
            lock (_sync)
            {
                return _whitelist;
            }
        }

        /// <summary>
        /// Reloads all three list files from <paramref name="o"/> and swaps snapshots under <see cref="_sync"/>.
        /// </summary>
        /// <param name="o">Current options snapshot (paths relative to <see cref="PostFilterOptions.DataDirectory"/>).</param>
        private void Reload(PostFilterOptions o)
        {
            string dir = o.DataDirectory.Trim();
            List<string> b = LoadLines(Path.Combine(dir, o.BanlistFileName));
            List<string> w = LoadLines(Path.Combine(dir, o.BadwordsFileName));
            List<string> wl = LoadLines(Path.Combine(dir, o.WhitelistFileName));
            lock (_sync)
            {
                _banlist = b;
                _badwords = w;
                _whitelist = wl;
            }

            PostFilterListRepositoryLog.ListsReloaded(_logger, b.Count, w.Count, wl.Count, null);
        }

        /// <summary>
        /// Reads non-comment, non-empty lines from a list file; returns an empty list when the file is missing or unreadable.
        /// </summary>
        /// <param name="path">Absolute path to the text file.</param>
        /// <returns>Trimmed lines (comments starting with <c>#</c> and blank lines are skipped).</returns>
        private List<string> LoadLines(string path)
        {
            List<string> list = [];
            try
            {
                if (!File.Exists(path))
                {
                    return list;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == '#')
                    {
                        continue;
                    }

                    list.Add(t);
                }
            }
            catch (Exception ex)
            {
                PostFilterListRepositoryLog.FailedToLoadListFile(_logger, ex, path);
            }

            return list;
        }

        /// <summary>High-performance logging helpers for <see cref="PostFilterListRepository"/>.</summary>
        private static partial class PostFilterListRepositoryLog
        {
            /// <summary>Delegate that logs successful list reload counts at debug level.</summary>
            public static readonly Action<ILogger, int, int, int, Exception?> ListsReloaded = LoggerMessage.Define<int, int, int>(
                LogLevel.Debug,
                new EventId(6500, nameof(ListsReloaded)),
                "PostFilter lists reloaded: banlist={Ban}, badwords={Bad}, whitelist={Wl}");

            [LoggerMessage(
                EventId = 6501,
                Level = LogLevel.Warning,
                Message = "PostFilter: failed to load list file {Path}")]
            public static partial void FailedToLoadListFile(ILogger logger, Exception exception, string path);
        }
    }
}

