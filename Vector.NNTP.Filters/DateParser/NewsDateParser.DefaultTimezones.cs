// <copyright file="NewsDateParser.DefaultTimezones.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// NewsDateParser.DefaultTimezones.cs -- Builds the frozen default abbreviation-to-offset table for NewsDateParser.
//
// Thread safety:
//   The mapping is constructed once at type initialization and then treated as immutable.

using System.Collections.Frozen;

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Default timezone abbreviation table partial for <see cref="NewsDateParser"/>.
    /// </summary>
    /// <remarks>
    /// <para>The table is frozen at type initialization; edits require changing this source and redeploying.</para>
    /// </remarks>
    public static partial class NewsDateParser
    {
        /// <summary>
        /// Builds the frozen trailing-abbreviation map used by <see cref="NewsDateParser"/>.
        /// </summary>
        /// <remarks>
        /// <para>Best-effort only: many abbreviations are ambiguous in the real world; this table favors parse success
        /// over geographic precision. <c>AMT</c> is mapped as Armenia (+04:00), not Amazon (see <c>AMST</c> for
        /// Manaus-style −03:00).</para>
        /// </remarks>
        /// <returns>A case-insensitive <see cref="FrozenDictionary{TKey, TValue}"/> from abbreviation to ISO-8601 offset string.</returns>
        private static FrozenDictionary<string, string> CreateDefaultTimezoneMappings()
        {
            Dictionary<string, string> d = new(StringComparer.OrdinalIgnoreCase)
            {
                ["ACDT"] = "+10:30",
                ["ACST"] = "+09:30",
                ["ADT"] = "-03:00",
                ["AEDT"] = "+11:00",
                ["AEST"] = "+10:00",
                ["AFT"] = "+04:30",
                ["AKDT"] = "-08:00",
                ["AKST"] = "-09:00",
                ["AMST"] = "-03:00",
                ["AMT"] = "+04:00",
                ["ART"] = "-03:00",
                ["AST"] = "-04:00",
                ["AWDT"] = "+09:00",
                ["AWST"] = "+08:00",
                ["BRST"] = "-02:00",
                ["BRT"] = "-03:00",
                ["BST"] = "+01:00",
                ["BST2"] = "+06:00",
                ["CAT"] = "+02:00",
                ["CDT"] = "-05:00",
                ["CEST"] = "+02:00",
                ["CET"] = "+01:00",
                ["ChST"] = "+10:00",
                ["CLST"] = "-03:00",
                ["CLT"] = "-04:00",
                ["CST"] = "+08:00",
                ["CST6CDT"] = "-06:00",
                ["EAT"] = "+03:00",
                ["EDT"] = "-04:00",
                ["EEST"] = "+03:00",
                ["EET"] = "+02:00",
                ["EST"] = "-05:00",
                ["GET"] = "+04:00",
                ["GMT"] = "+00:00",
                ["GST"] = "+04:00",
                ["HDT"] = "-09:00",
                ["HKT"] = "+08:00",
                ["HST"] = "-10:00",
                ["ICT"] = "+07:00",
                ["IDT"] = "+03:00",
                ["IRST"] = "+03:30",
                ["IST"] = "+05:30",
                ["JST"] = "+09:00",
                ["KST"] = "+09:00",
                ["MDT"] = "-06:00",
                ["MESZ"] = "+02:00",
                ["MET"] = "+01:00",
                ["MEZ"] = "+01:00",
                ["MMT"] = "+06:30",
                ["MSK"] = "+03:00",
                ["MST"] = "-07:00",
                ["MYT"] = "+08:00",
                ["NDT"] = "-02:30",
                ["NPT"] = "+05:45",
                ["NST"] = "-03:30",
                ["NZDT"] = "+13:00",
                ["NZST"] = "+12:00",
                ["IRKT"] = "+08:00",
                ["KRAT"] = "+07:00",
                ["MAGT"] = "+11:00",
                ["OMST"] = "+06:00",
                ["PETT"] = "+12:00",
                ["PHT"] = "+08:00",
                ["VLAT"] = "+10:00",
                ["YAKT"] = "+09:00",
                ["PDT"] = "-07:00",
                ["PKT"] = "+05:00",
                ["PST"] = "-08:00",
                ["SAST"] = "+02:00",
                ["SGT"] = "+08:00",
                ["SST"] = "+08:00",
                ["SYOT"] = "+03:00",
                ["THA"] = "+07:00",
                ["TJT"] = "+05:00",
                ["TMT"] = "+05:00",
                ["TRT"] = "+03:00",
                ["ULAT"] = "+08:00",
                ["UT"] = "+00:00",
                ["UTC"] = "+00:00",
                ["UTC+0"] = "+00:00",
                ["UTC+1"] = "+01:00",
                ["UTC+2"] = "+02:00",
                ["UTC+3"] = "+03:00",
                ["UTC+4"] = "+04:00",
                ["UTC+5"] = "+05:00",
                ["UTC+6"] = "+06:00",
                ["UTC+7"] = "+07:00",
                ["UTC+8"] = "+08:00",
                ["UTC+9"] = "+09:00",
                ["UTC-1"] = "-01:00",
                ["UTC-2"] = "-02:00",
                ["UTC-3"] = "-03:00",
                ["UTC-4"] = "-04:00",
                ["UTC-5"] = "-05:00",
                ["UTC-6"] = "-06:00",
                ["UTC-7"] = "-07:00",
                ["UTC-8"] = "-08:00",
                ["UTC-9"] = "-09:00",
                ["VET"] = "-04:00",
                ["WAT"] = "+01:00",
                ["WEST"] = "+01:00",
                ["WET"] = "+00:00",
                ["WIB"] = "+07:00",
                ["WIT"] = "+09:00",
                ["WITA"] = "+08:00",
            };

            return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }
}

