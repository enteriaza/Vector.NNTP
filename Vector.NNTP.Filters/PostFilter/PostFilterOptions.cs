// <copyright file="PostFilterOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterOptions.cs -- JSON-bound options for the local posting filter (replaces Perl postfilter.conf tuples).
//
// Thread safety:
//   Options instances are not thread-safe; bind via IOptionsMonitor and treat snapshots as read-only after validation.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// JSON-bound options for the local posting filter (replaces Perl <c>postfilter.conf</c> tuples).
    /// </summary>
    /// <remarks>
    /// <para><b>Binding:</b> Bind from configuration section <see cref="SectionName"/>.</para>
    /// <para><b>Validation:</b> Use <see cref="PostFilterOptionsValidator"/> and DataAnnotations validation on startup.</para>
    /// <para><b>Lists:</b> Banlist, badwords, and whitelist paths are resolved under <see cref="DataDirectory"/> by <see cref="PostFilterListRepository"/>.</para>
    /// </remarks>
    public sealed class PostFilterOptions
    {
        /// <summary>Configuration section name used when binding from <c>appsettings.json</c> (<c>PostFilter</c>).</summary>
        public const string SectionName = "PostFilter";

        /// <summary>
        /// Root directory containing banlist, badwords, and whitelist text files referenced by the relative file name properties.
        /// </summary>
        public string DataDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Global posting gate: <see cref="PostFilterServerStatus.Active"/> runs the pipeline;
        /// <see cref="PostFilterServerStatus.Closed"/> rejects all posts;
        /// <see cref="PostFilterServerStatus.Disabled"/> bypasses checks.
        /// </summary>
        public PostFilterServerStatus ServerStatus { get; set; } = PostFilterServerStatus.Active;

        /// <summary>
        /// Determines whether rate limits and identity classification key on client IP, authenticated username, or both.
        /// </summary>
        public PostFilterServerType ServerType { get; set; } = PostFilterServerType.Public;

        /// <summary>
        /// Entropy mixed into optional client fingerprint headers (Perl <c>salt</c>); must be non-trivial when header rewrite is enabled.
        /// </summary>
        [MinLength(1)]
        public string Salt { get; set; } = "change-me";

        /// <summary>
        /// When <see langword="true"/>, enables reverse-DNS domain checks comparable to Perl <c>enable_domain_check</c>.
        /// </summary>
        public bool EnableDomainCheck { get; set; }

        /// <summary>
        /// Regular expression identifying usernames treated as public identities when <see cref="ServerType"/> is <see cref="PostFilterServerType.Both"/>.
        /// </summary>
        public string PublicUserIdPattern { get; set; } = "^default$";

        /// <summary>
        /// When <see langword="true"/>, whitelist matches take a shortened pipeline (banlist, style, custom handlers only).
        /// </summary>
        public bool CheckWhiteList { get; set; }

        /// <summary>When <see langword="true"/>, DNS RBL queries run for IPv4 clients.</summary>
        public bool CheckRbl { get; set; }

        /// <summary>When <see langword="true"/>, URIBL queries run against URL hosts found in the article body.</summary>
        public bool CheckUribl { get; set; }

        /// <summary>When <see langword="true"/>, Tor exit DNS checks run for IPv4 clients.</summary>
        public bool CheckTor { get; set; }

        /// <summary>When <see langword="true"/>, banlist substring scanning runs against From, Subject, and body text.</summary>
        public bool CheckBanlist { get; set; }

        /// <summary>When <see langword="true"/>, badwords substring scanning runs against From, Subject, and body text.</summary>
        public bool CheckBadwords { get; set; }

        /// <summary>When <see langword="true"/>, per-identity posting rate limits are enforced.</summary>
        public bool CheckUsers { get; set; }

        /// <summary>When <see langword="true"/>, registered <see cref="IPostFilterCustomHandler"/> implementations run after built-in checks.</summary>
        public bool CheckCustom { get; set; }

        /// <summary>
        /// When <see langword="true"/>, numeric rejection codes are included in client-visible messages via <see cref="PostFilterRejectionMessages"/>.
        /// </summary>
        public bool ShowErrorCode { get; set; } = true;

        /// <summary>
        /// Perl <c>default_action_on_accept</c> semantics applied after all checks pass (accept, discard, invert, etc.).
        /// </summary>
        public PostFilterDefaultAction DefaultActionOnAccept { get; set; } = PostFilterDefaultAction.Accept;

        /// <summary>
        /// Perl <c>default_action_on_reject</c> semantics applied after a check fails (reject, accept anyway, discard, etc.).
        /// </summary>
        public PostFilterDefaultAction DefaultActionOnReject { get; set; } = PostFilterDefaultAction.Reject;

        /// <summary>Relative path under <see cref="DataDirectory"/> for banlist lines (one entry per line, <c>#</c> comments allowed).</summary>
        public string BanlistFileName { get; set; } = "banlist.txt";

        /// <summary>Relative path under <see cref="DataDirectory"/> for badword substrings.</summary>
        public string BadwordsFileName { get; set; } = "badwords.txt";

        /// <summary>Relative path under <see cref="DataDirectory"/> for whitelist patterns (From substring, username, or IP prefix).</summary>
        public string WhitelistFileName { get; set; } = "whitelist.txt";

        /// <summary>DNS blocklist and Tor query tuning (timeouts and zone suffix lists).</summary>
        public PostFilterDnsOptions Dns { get; set; } = new();

        /// <summary>Sliding-window posting rate limits (simplified Perl <c>access.conf</c>).</summary>
        public PostFilterAccessOptions Access { get; set; } = new();

        /// <summary>Header and article shape checks (subset of Perl <c>style.pm</c>).</summary>
        public PostFilterStyleOptions Style { get; set; } = new();

        /// <summary>Optional header transforms applied on accept (subset of Perl <c>mod_headers</c>).</summary>
        public PostFilterHeaderRewriteOptions HeaderRewrite { get; set; } = new();
    }

    /// <summary>DNS blocklist query tuning for the postfilter pipeline.</summary>
    /// <remarks>
    /// <para><b>Failure policy:</b> DNS failures are treated as not listed (fail open) for availability in the MVP resolver.</para>
    /// </remarks>
    public sealed class PostFilterDnsOptions
    {
        /// <summary>Per-query DNS timeout in milliseconds for RBL, URIBL, and Tor lookups.</summary>
        [Range(100, 120_000)]
        public int QueryTimeoutMilliseconds { get; set; } = 2500;

        /// <summary>
        /// RBL zone suffixes (for example <c>zen.spamhaus.org</c>); client IPv4 is reversed and appended as labels.
        /// </summary>
        public List<string> RblZones { get; set; } = new();

        /// <summary>
        /// URIBL zone suffixes; each discovered hostname is queried as <c>{host}.{zone}</c>.
        /// </summary>
        public List<string> UriblZones { get; set; } = new();

        /// <summary>
        /// Tor DNS suffix used for exit-node checks (default matches Tor Project <c>dnsel.torproject.org</c> style usage).
        /// </summary>
        public string TorDnsSuffix { get; set; } = "dnsel.torproject.org";
    }

    /// <summary>Posting rate limits (simplified Perl <c>access.conf</c>).</summary>
    /// <remarks>
    /// <para>Limits are enforced in-process by <see cref="PostFilterAccessTracker"/>; set max posts to <c>0</c> to disable a limit class.</para>
    /// </remarks>
    public sealed class PostFilterAccessOptions
    {
        /// <summary>Sliding window length in seconds for unauthenticated clients keyed by IP address.</summary>
        [Range(1, 86400)]
        public int PublicIpWindowSeconds { get; set; } = 600;

        /// <summary>Maximum posts per <see cref="PublicIpWindowSeconds"/> window for a single public IP (0 = unlimited).</summary>
        [Range(0, 1_000_000)]
        public int PublicIpMaxPostsPerWindow { get; set; } = 30;

        /// <summary>Sliding window length in seconds for authenticated posters keyed by username.</summary>
        [Range(1, 86400)]
        public int AuthUserWindowSeconds { get; set; } = 600;

        /// <summary>Maximum posts per <see cref="AuthUserWindowSeconds"/> window for a single username (0 = unlimited).</summary>
        [Range(0, 1_000_000)]
        public int AuthUserMaxPostsPerWindow { get; set; } = 50;
    }

    /// <summary>Header and article shape checks (subset of Perl <c>style.pm</c>).</summary>
    public sealed class PostFilterStyleOptions
    {
        /// <summary>
        /// Header field names that must not be present (case-insensitive); presence triggers rejection code 4.
        /// </summary>
        public List<string> ForbiddenHeaderNames { get; set; } = new();

        /// <summary>
        /// Maximum distinct newsgroups allowed in a <c>Newsgroups</c> header (0 disables the crosspost limit).
        /// </summary>
        [Range(0, 10_000)]
        public int MaxNewsgroupCrossposts { get; set; } = 7;

        /// <summary>
        /// Maximum article size in bytes including headers (0 disables); oversized articles reject with code 12.
        /// </summary>
        [Range(0, long.MaxValue)]
        public long MaxArticleBytes { get; set; } = 1_048_576;
    }

    /// <summary>Optional header transforms applied on accept (subset of Perl <c>mod_headers</c> behavior).</summary>
    public sealed class PostFilterHeaderRewriteOptions
    {
        /// <summary>
        /// When <see langword="true"/>, removes <c>NNTP-Posting-Host</c> from accepted articles before storage.
        /// </summary>
        public bool StripNntpPostingHost { get; set; }

        /// <summary>
        /// When <see langword="true"/>, appends <c>X-Postfilter-Client-Token</c> (BLAKE3 hex over UTF-8 salt + client IP; 64 lowercase hex characters).
        /// </summary>
        public bool AppendClientTokenHeader { get; set; }
    }
}
