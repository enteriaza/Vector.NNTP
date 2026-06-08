// <copyright file="ArticleTypeMetricsTags.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: maps ArticleTypeFlags bits to OpenTelemetry article_type_total type tag strings.

using Vector.NNTP.Articles.Classification;

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps <see cref="ArticleTypeFlags"/> bits to stable <c>article_type_total</c> OpenTelemetry <c>type</c> tag values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Sole mapping table for <see cref="NntpSpoolMetrics.RecordArticleTypes"/>. Each set mapped bit on an
    /// accepted article increments <c>article_type_total</c> once with the corresponding <c>type</c> tag. Tags use
    /// snake_case for Prometheus-style exporters.
    /// </para>
    /// <para>
    /// <b>Multi-flag articles:</b> Classification may set multiple bits (for example <see cref="ArticleTypeFlags.YEnc"/>
    /// and <see cref="ArticleTypeFlags.Binary"/>); metrics emit one increment per mapped bit that is present.
    /// </para>
    /// <para>
    /// <b>Default bucket:</b> When no mapped bit is set (typically <see cref="ArticleTypeFlags.Default"/> only),
    /// <see cref="NntpSpoolMetrics.RecordArticleTypes"/> emits <see cref="DefaultTag"/> once so plain-text volume remains
    /// visible.
    /// </para>
    /// <para>
    /// <b>Extension contract:</b> When adding a new <see cref="ArticleTypeFlags"/> value consumed by
    /// <see cref="ArticleTypeClassifier"/>, add a matching entry to <see cref="MappedTags"/> here (and tests) so
    /// dashboards stay aligned. Unmapped classifier bits are omitted from metrics; they do not trigger
    /// <see cref="DefaultTag"/> unless no mapped bit is set.
    /// </para>
    /// <para><b>Threading:</b> Static read-only data; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class ArticleTypeMetricsTags
    {
        /// <summary>
        /// OpenTelemetry <c>type</c> tag value emitted when no mapped classification flag is present on an accepted article.
        /// </summary>
        /// <remarks>
        /// Literal <c>default</c>. Used by <see cref="NntpSpoolMetrics.RecordArticleTypes"/> when the article type mask
        /// intersects none of the flags in <see cref="MappedTags"/> (equivalently when
        /// <c>(articleType &amp; <see cref="MappedFlagsMask"/>) == 0</c>).
        /// </remarks>
        internal const string DefaultTag = "default";

        /// <summary>
        /// Ordered flag-to-tag pairs iterated by <see cref="NntpSpoolMetrics.RecordArticleTypes"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order is stable definition order in this array (not ascending bit index). Each tuple maps one
        /// <see cref="ArticleTypeFlags"/> bit to a snake_case <c>type</c> tag string on <c>article_type_total</c>.
        /// </para>
        /// <para>
        /// Includes every classifiable flag except <see cref="ArticleTypeFlags.Default"/> and reserved unused bits in
        /// <see cref="ArticleTypeFlags"/>. Tag strings are part of the external metrics contract — change only with
        /// dashboard migration.
        /// </para>
        /// </remarks>
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
        /// Bitmask of all <see cref="ArticleTypeFlags"/> values that map to explicit <c>type</c> tags in
        /// <see cref="MappedTags"/>.
        /// </summary>
        /// <value>
        /// OR-combination of every <see cref="ArticleTypeFlags"/> entry in <see cref="MappedTags"/>; excludes
        /// <see cref="ArticleTypeFlags.Default"/> and reserved bits.
        /// </value>
        /// <remarks>
        /// Computed once at type initialization by <see cref="ComputeMappedFlagsMask"/>. Useful for tests and future
        /// metrics logic that needs to know whether an article type mask contains any mapped classification bit.
        /// </remarks>
        internal static ArticleTypeFlags MappedFlagsMask { get; } = ComputeMappedFlagsMask();

        /// <summary>
        /// Returns the mapped tag entries for metrics emission.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadOnlySpan{T}"/> view over the static <see cref="MappedTags"/> array. The span is valid for
        /// the process lifetime and reflects the compile-time mapping table.
        /// </returns>
        /// <remarks>
        /// Called from <see cref="NntpSpoolMetrics.RecordArticleTypes"/> on the hot path for each accepted article.
        /// Never allocates. Never throws.
        /// </remarks>
        internal static ReadOnlySpan<(ArticleTypeFlags Flag, string Tag)> GetMappedTags()
        {
            return MappedTags;
        }

        /// <summary>
        /// Builds <see cref="MappedFlagsMask"/> by OR-ing every flag present in <see cref="MappedTags"/>.
        /// </summary>
        /// <returns>
        /// Combined bitmask of all mapped <see cref="ArticleTypeFlags"/> values.
        /// </returns>
        /// <remarks>
        /// Invoked once during static initialization of <see cref="MappedFlagsMask"/>. Never throws.
        /// </remarks>
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
