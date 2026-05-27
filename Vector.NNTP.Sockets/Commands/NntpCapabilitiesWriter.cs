// <copyright file="NntpCapabilitiesWriter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: builds CAPABILITIES multi-line body.

namespace Vector.NNTP.Sockets.Commands
{
    /// <summary>
    /// Collects capability lines for CAPABILITIES command responses.
    /// </summary>
    public sealed class NntpCapabilitiesWriter
    {
        private readonly List<string> _lines = new();

        /// <summary>
        /// Appends a capability line.
        /// </summary>
        /// <param name="line">Capability keyword or argument line.</param>
        public void AppendLine(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            this._lines.Add(line);
        }

        /// <summary>
        /// Gets the collected lines.
        /// </summary>
        /// <returns>Read-only list of capability lines.</returns>
        public IReadOnlyList<string> ToLines() => this._lines;
    }
}
