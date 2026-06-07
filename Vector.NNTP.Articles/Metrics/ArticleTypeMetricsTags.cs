// <copyright file="ArticleTypeMetricsTags.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: maps ArticleTypeFlags bits to OpenTelemetry article_type_total type tag strings.

using Vector.NNTP.Articles.Classification;

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps <see cref="ArticleTypeFlags"/> bits to <c>article_type_total</c> OpenTelemetry <c>type</c> tag values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Articles with multiple flags increment multiple counters (for example <see cref="ArticleTypeFlags.YEnc"/> and
    /// <see cref="ArticleTypeFlags.Binary"/> both emit). When no mapped flag is set, callers emit <c>default</c> once.
    /// </para>
    /// <para>Add new entries here when extending <see cref="ArticleTypeFlags"/> so dashboards stay in sync.</para>
    /// </remarks>
    internal static class ArticleTypeMetricsTags
    {
        /// <summary>
        /// OpenTelemetry tag value emitted when no mapped classification flag is present.
        /// </summary>
        internal const string DefaultTag = "default";

        /// <summary>
        /// Ordered flag-to-tag pairs iterated by <see cref="NntpSpoolMetrics.RecordArticleTypes"/>.
        /// </summary>
        private static readonly (ArticleTypeFlags Flag, string Tag)[] MappedTags =
        [
            (ArticleTypeFlags.YEnc, "yenc"),
            (ArticleTypeFlags.Binary, "binary"),
            (ArticleTypeFlags.UuEncode, "uuencode"),
            (ArticleTypeFlags.Base64, "base64"),
            (ArticleTypeFlags.BinHex, "binhex"),
            (ArticleTypeFlags.Text, "text"),
            (ArticleTypeFlags.Html, "html"),
            (ArticleTypeFlags.Archive, "archive"),
            (ArticleTypeFlags.Image, "image"),
            (ArticleTypeFlags.Video, "video"),
            (ArticleTypeFlags.Audio, "audio"),
            (ArticleTypeFlags.Mime, "mime"),
            (ArticleTypeFlags.Multipart, "multipart"),
            (ArticleTypeFlags.Control, "control"),
            (ArticleTypeFlags.Cancel, "cancel"),
            (ArticleTypeFlags.Approved, "approved"),
            (ArticleTypeFlags.Supersedes, "supersedes"),
            (ArticleTypeFlags.PgpSigned, "pgp_signed"),
            (ArticleTypeFlags.Smime, "smime"),
            (ArticleTypeFlags.MultipartMixed, "multipart_mixed"),
            (ArticleTypeFlags.MultipartAlternative, "multipart_alternative"),
            (ArticleTypeFlags.MultipartRelated, "multipart_related"),
            (ArticleTypeFlags.MultipartSigned, "multipart_signed"),
            (ArticleTypeFlags.Partial, "partial"),
            (ArticleTypeFlags.PgpMessage, "pgp_message"),
            (ArticleTypeFlags.NzbGenerated, "nzb_generated"),
            (ArticleTypeFlags.MassCrosspost, "mass_crosspost"),
            (ArticleTypeFlags.FollowupRedirect, "followup_redirect"),
            (ArticleTypeFlags.SignedControl, "signed_control"),
        ];

        /// <summary>
        /// Bitmask of all flags that map to explicit <c>type</c> tags (excludes <see cref="ArticleTypeFlags.Default"/>).
        /// </summary>
        internal static ArticleTypeFlags MappedFlagsMask { get; } = ComputeMappedFlagsMask();

        /// <summary>
        /// Returns the mapped tag entries for metrics emission.
        /// </summary>
        /// <returns>Read-only view of flag-to-tag pairs.</returns>
        internal static ReadOnlySpan<(ArticleTypeFlags Flag, string Tag)> GetMappedTags()
        {
            return MappedTags;
        }

        private static ArticleTypeFlags ComputeMappedFlagsMask()
        {
            ArticleTypeFlags mask = ArticleTypeFlags.Default;
            foreach ((ArticleTypeFlags flag, _) in MappedTags)
            {
                mask |= flag;
            }

            return mask;
        }
    }
}
