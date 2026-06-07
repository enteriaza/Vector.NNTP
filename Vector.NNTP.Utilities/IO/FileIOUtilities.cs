// <copyright file="FileIOUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// FileIOUtilities.cs -- Unified I/O utility class consolidating file and directory durability primitives.

namespace Vector.NNTP.Utilities.IO
{
    /// <summary>
    /// File and directory I/O helpers: atomic file writes, resilient reads, best-effort secure permissions, and
    /// best-effort directory metadata flush on Linux.
    /// </summary>
    /// <remarks>
    /// <para><b>Atomic writes:</b> Writes are performed to a temp file in the same directory, flushed to stable storage,
    /// then atomically renamed into place. On Linux, a best-effort directory fsync is also attempted.</para>
    /// </remarks>
    public static class FileIOUtilities
    {
        /// <summary>
        /// File permission mode applied to written files on Linux: owner read/write only (0600).
        /// </summary>
        public const UnixFileMode SecureFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        /// <summary>
        /// Directory permission mode for sensitive directories on Linux: owner read/write/execute only (0700).
        /// </summary>
        public const UnixFileMode SecureDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        /// <summary>
        /// Writes binary content to a file atomically via a temp file, fsync, and rename.
        /// </summary>
        /// <param name="path">The target file path.</param>
        /// <param name="content">The binary content to write.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when the write, flush, rename, and directory fsync (Linux best-effort) complete.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is canceled during I/O.</exception>
        /// <exception cref="IOException">Propagated when temp write, flush, rename, or cleanup fails.</exception>
        public static async Task AtomicWriteAsync(string path, byte[] content, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentNullException.ThrowIfNull(content);

            string directory = Path.GetDirectoryName(path)!;
            string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");

            try
            {
                FileStream fs = new(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                try
                {
                    SetSecureFilePermissions(tempPath);

                    await fs.WriteAsync(content, ct).ConfigureAwait(false);

                    fs.Flush(flushToDisk: true);
                }
                finally
                {
                    await fs.DisposeAsync().ConfigureAwait(false);
                }

                File.Move(tempPath, path, overwrite: true);

                SetSecureFilePermissions(path);
                FsyncDirectory(directory);
            }
            catch
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // Best-effort cleanup.
                }

                throw;
            }
        }

        /// <summary>
        /// Reads a file using <paramref name="readFunc"/>, returning <see langword="null"/> when the file is absent or
        /// unreadable. Cancellation is propagated.
        /// </summary>
        /// <typeparam name="T">Return type.</typeparam>
        /// <param name="readFunc">Async file read delegate.</param>
        /// <param name="path">File path.</param>
        /// <param name="onError">Optional callback invoked on unexpected errors.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The file content or <see langword="null"/> when the file is missing or an unexpected error occurs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="readFunc"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is canceled; not swallowed.</exception>
        /// <remarks>
        /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> are converted to
        /// <see langword="null"/>. Other exceptions invoke <paramref name="onError"/> when supplied and also return
        /// <see langword="null"/>.
        /// </remarks>
        public static async Task<T?> TryReadFileAsync<T>(
            Func<string, CancellationToken, Task<T>> readFunc,
            string path,
            Action<Exception>? onError,
            CancellationToken ct)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(readFunc);
            ArgumentException.ThrowIfNullOrEmpty(path);

            try
            {
                return await readFunc(path, ct).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                return null;
            }
        }

        /// <summary>
        /// Sets <see cref="SecureFileMode"/> (0600) on the specified file when running on Linux. No-op on Windows.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="onError">Optional callback invoked when the permission change fails.</param>
        public static void SetSecureFilePermissions(string path, Action<string, Exception>? onError = null)
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(path, SecureFileMode);
            }
            catch (Exception ex)
            {
                try
                {
                    onError?.Invoke(path, ex);
                }
                catch (Exception)
                {
                    // Callback fault isolation.
                }
            }
        }

        /// <summary>
        /// Sets <see cref="SecureDirectoryMode"/> (0700) on the specified directory when running on Linux. No-op on
        /// Windows.
        /// </summary>
        /// <param name="path">Directory path.</param>
        /// <returns>The exception thrown, or <see langword="null"/> on success or when not running on Linux.</returns>
        public static Exception? TrySetSecureDirectoryPermissions(string path)
        {
            if (!OperatingSystem.IsLinux())
            {
                return null;
            }

            try
            {
                File.SetUnixFileMode(path, SecureDirectoryMode);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Flushes a directory's metadata to stable storage on Linux (best-effort). No-op on Windows.
        /// </summary>
        /// <param name="directoryPath">Directory path.</param>
        public static void FsyncDirectory(string directoryPath)
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            try
            {
                using FileStream dirFs = new(directoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                dirFs.Flush(flushToDisk: true);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }
    }
}
