// <copyright file="CgroupPathResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: resolves Linux cgroup directory paths for the current process.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Maps Linux <c>/proc/self/cgroup</c> membership lines to cgroup filesystem directories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="LinuxCgroupCpuUsageSampler"/> to locate quota and usage control files.
    /// Supports cgroup v2 unified hierarchy lines (<c>0::/slice/path</c>) and v1 hybrid lines that list
    /// <c>cpu</c> or <c>cpuacct</c> controllers.
    /// </para>
    /// <para>
    /// Resolution prefers v2 when a controller-free hierarchy line is present. For v1, candidate paths under
    /// <c>cpu,cpuacct</c> and <c>cpu</c> mount layouts are tested with <see cref="Directory.Exists(string)"/>.
    /// </para>
    /// </remarks>
    public static class CgroupPathResolver
    {
        /// <summary>
        /// Default unified cgroup v2 mount root on Linux (<c>/sys/fs/cgroup</c>).
        /// </summary>
        public const string DefaultCgroupRoot = "/sys/fs/cgroup";

        /// <summary>
        /// Attempts to resolve the cgroup directory for the current process using default paths.
        /// </summary>
        /// <param name="cgroupDirectory">
        /// When this method returns <see langword="true"/>, receives the absolute cgroup directory path.
        /// </param>
        /// <param name="isV2">
        /// When this method returns <see langword="true"/>, receives whether cgroup v2 layout was detected.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <see cref="TryResolveFromCgroupFile"/> succeeds for
        /// <c>/proc/self/cgroup</c> and <see cref="DefaultCgroupRoot"/>.
        /// </returns>
        public static bool TryResolveCurrent(out string cgroupDirectory, out bool isV2)
        {
            return TryResolveFromCgroupFile("/proc/self/cgroup", DefaultCgroupRoot, out cgroupDirectory, out isV2);
        }

        /// <summary>
        /// Attempts to resolve a cgroup directory from <c>/proc/self/cgroup</c> text and a cgroup mount root.
        /// </summary>
        /// <param name="procSelfCgroupContent">Full or partial contents of <c>/proc/self/cgroup</c>.</param>
        /// <param name="cgroupRoot">Cgroup filesystem root (for example <see cref="DefaultCgroupRoot"/>).</param>
        /// <param name="cgroupDirectory">
        /// When this method returns <see langword="true"/>, receives the resolved absolute directory.
        /// </param>
        /// <param name="isV2">
        /// When this method returns <see langword="true"/>, receives <see langword="true"/> for v2 unified hierarchy
        /// or <see langword="false"/> for v1 cpu controller layout.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a cgroup directory was resolved and exists on disk (v2 root path <c>/</c>
        /// is accepted without an existence check); otherwise <see langword="false"/> and
        /// <paramref name="cgroupDirectory"/> is empty.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="procSelfCgroupContent"/> or <paramref name="cgroupRoot"/> is null or empty.
        /// </exception>
        /// <remarks>
        /// <para>Line format: <c>hierarchy:controllers:path</c>.</para>
        /// <list type="number">
        /// <item><description>v2 — empty <c>controllers</c> field (for example <c>0::/system.slice/app.service</c>).</description></item>
        /// <item><description>v1 — <c>controllers</c> contains <c>cpu</c> or <c>cpuacct</c>.</description></item>
        /// </list>
        /// </remarks>
        public static bool TryResolveFromCgroupFile(
            string procSelfCgroupContent,
            string cgroupRoot,
            out string cgroupDirectory,
            out bool isV2)
        {
            cgroupDirectory = string.Empty;
            isV2 = false;
            ArgumentException.ThrowIfNullOrEmpty(procSelfCgroupContent);
            ArgumentException.ThrowIfNullOrEmpty(cgroupRoot);

            string? v2Path = null;
            string? v1CpuPath = null;

            foreach (string rawLine in procSelfCgroupContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                int firstColon = line.IndexOf(':');
                int lastColon = line.LastIndexOf(':');
                if (firstColon < 0 || lastColon <= firstColon)
                {
                    continue;
                }

                string controllers = line.Substring(firstColon + 1, lastColon - firstColon - 1);
                string path = line[(lastColon + 1)..];
                if (controllers.Length == 0)
                {
                    v2Path = path;
                    continue;
                }

                if (controllers.Contains("cpu", StringComparison.Ordinal) ||
                    controllers.Contains("cpuacct", StringComparison.Ordinal))
                {
                    v1CpuPath = path;
                }
            }

            if (v2Path is not null)
            {
                isV2 = true;
                cgroupDirectory = CombineCgroupPath(cgroupRoot, v2Path);
                return Directory.Exists(cgroupDirectory) || v2Path == "/";
            }

            if (v1CpuPath is not null)
            {
                isV2 = false;
                string combined = Path.Combine(cgroupRoot, "cpu,cpuacct", v1CpuPath.TrimStart('/'));
                if (Directory.Exists(combined))
                {
                    cgroupDirectory = combined;
                    return true;
                }

                combined = Path.Combine(cgroupRoot, "cpu", v1CpuPath.TrimStart('/'));
                if (Directory.Exists(combined))
                {
                    cgroupDirectory = combined;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Combines a cgroup mount root with a relative hierarchy path from <c>/proc/self/cgroup</c>.
        /// </summary>
        /// <param name="root">Cgroup filesystem root directory.</param>
        /// <param name="relativePath">Relative path after the final colon (may be <c>/</c> for root).</param>
        /// <returns>
        /// <paramref name="root"/> when <paramref name="relativePath"/> is <c>/</c> or empty;
        /// otherwise <paramref name="root"/> joined with each path segment using forward-slash splitting
        /// so mixed separators in cgroup lines normalize correctly on the host OS.
        /// </returns>
        private static string CombineCgroupPath(string root, string relativePath)
        {
            if (relativePath == "/" || string.IsNullOrEmpty(relativePath))
            {
                return root;
            }

            string[] segments = relativePath.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string combined = root;
            foreach (string segment in segments)
            {
                combined = Path.Combine(combined, segment);
            }

            return combined;
        }
    }
}
