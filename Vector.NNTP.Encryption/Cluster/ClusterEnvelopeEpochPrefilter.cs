// <copyright file="ClusterEnvelopeEpochPrefilter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text.Json;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Cheap UTF-8 scan of cluster envelope JSON for <c>payloadType</c> and nested <c>payload.epoch</c>.
    /// </summary>
    internal static partial class ClusterEnvelopeEpochPrefilter
    {
        /// <summary>
        /// Tries to read <c>payloadType</c> and optionally <c>payload.epoch</c> without full deserialization.
        /// </summary>
        /// <param name="utf8Json">Uncompressed envelope JSON.</param>
        /// <param name="payloadType">Wire payload type string, or null when absent.</param>
        /// <param name="epochPresent">True when a numeric epoch was read under payload.</param>
        /// <param name="epochValue">Epoch value when <paramref name="epochPresent"/> is true.</param>
        /// <param name="logger">Optional logger for malformed JSON diagnostics.</param>
        /// <returns>False when JSON is not a well-formed root object for this scan.</returns>
        internal static bool TryReadEnvelopePayloadTypeAndClusterEpoch(
            ReadOnlySpan<byte> utf8Json,
            out string? payloadType,
            out bool epochPresent,
            out long epochValue,
            ILogger? logger = null)
        {
            payloadType = null;
            epochPresent = false;
            epochValue = 0;

            try
            {
                Utf8JsonReader reader = new(utf8Json, isFinalBlock: true, state: default);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return false;

                while (reader.Read())
                {
                    if (payloadType is not null && epochPresent)
                        return true;

                    if (reader.TokenType == JsonTokenType.EndObject)
                        return true;

                    if (reader.TokenType != JsonTokenType.PropertyName)
                        return false;

                    if (reader.ValueTextEquals("payloadType"u8))
                    {
                        if (!reader.Read())
                            return false;

                        if (reader.TokenType == JsonTokenType.String)
                            payloadType = reader.GetString();
                        else
                            reader.Skip();
                    }
                    else if (reader.ValueTextEquals("payload"u8))
                    {
                        if (!reader.Read())
                            return false;

                        if (reader.TokenType != JsonTokenType.StartObject)
                        {
                            reader.Skip();
                            continue;
                        }

                        while (reader.Read())
                        {
                            if (payloadType is not null && epochPresent)
                                return true;

                            if (reader.TokenType == JsonTokenType.EndObject)
                                break;

                            if (reader.TokenType != JsonTokenType.PropertyName)
                                return false;

                            if (reader.ValueTextEquals("epoch"u8))
                            {
                                if (!reader.Read())
                                    return false;

                                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long v))
                                {
                                    epochPresent = true;
                                    epochValue = v;
                                }
                                else
                                {
                                    reader.Skip();
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return true;
            }
            catch (JsonException ex)
            {
                if (logger is not null)
                {
                    LogEnvelopePrefilterJsonFailed(logger, ex.GetType().Name, ex);
                }

                return false;
            }
        }
    }
}
