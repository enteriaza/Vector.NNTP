// <copyright file="NntpBindAddressNormalizer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: IPv4/IPv6 bind-address normalization for listener startup and options validation.

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Normalizes configured bind-address strings into <see cref="IPAddress"/> values for
    /// <see cref="Hosting.NntpSocketAcceptor"/> listeners.
    /// </summary>
    internal static class NntpBindAddressNormalizer
    {
        /// <summary>
        /// Resolves an IPv4 bind address from configuration text.
        /// </summary>
        /// <param name="address">Configured <see cref="NntpServerOptions.BindAddress"/> value.</param>
        /// <param name="ip">Resolved IPv4 address when this method returns <see langword="true"/>.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="address"/> is empty, <c>*</c>, or a parseable IPv4 literal;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Empty and <c>*</c> map to <see cref="IPAddress.Any"/> (<c>0.0.0.0</c>). IPv6 literals are rejected.
        /// </remarks>
        public static bool TryResolveIpv4BindAddress(string? address, out IPAddress ip)
        {
            ip = IPAddress.Any;
            if (string.IsNullOrWhiteSpace(address) || address.Trim() == "*")
            {
                return true;
            }

            if (!IPAddress.TryParse(address.Trim(), out IPAddress? parsed) || parsed is null)
            {
                return false;
            }

            if (parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            ip = parsed;
            return true;
        }

        /// <summary>
        /// Resolves an IPv6 bind address from configuration text.
        /// </summary>
        /// <param name="address">Configured <see cref="NntpServerOptions.BindAddress6"/> value.</param>
        /// <param name="ip">
        /// Resolved IPv6 address when this method returns <see langword="true"/>; otherwise <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="address"/> is non-empty and resolves to an IPv6 address;
        /// <see langword="false"/> when <paramref name="address"/> is empty (IPv6 listener disabled) or invalid.
        /// </returns>
        /// <remarks>
        /// <c>*</c> and <c>::</c> map to <see cref="IPAddress.IPv6Any"/>. IPv4 literals are rejected.
        /// </remarks>
        public static bool TryResolveIpv6BindAddress(string? address, out IPAddress? ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            string trimmed = address.Trim();
            if (trimmed == "*" || trimmed == "::")
            {
                ip = IPAddress.IPv6Any;
                return true;
            }

            if (!IPAddress.TryParse(trimmed, out IPAddress? parsed) || parsed is null)
            {
                return false;
            }

            if (parsed.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            ip = parsed;
            return true;
        }

        /// <summary>
        /// Validates <see cref="NntpServerOptions.BindAddress"/> for startup binding.
        /// </summary>
        /// <param name="bindAddress">Configured IPv4 bind address.</param>
        /// <returns>
        /// A <see cref="ValidateOptionsResult"/> failure when the value is not a valid IPv4 bind target; otherwise
        /// <see langword="null"/>.
        /// </returns>
        public static ValidateOptionsResult? ValidateBindAddress(string? bindAddress)
        {
            if (TryResolveIpv4BindAddress(bindAddress, out _))
            {
                return null;
            }

            return ValidateOptionsResult.Fail(
                $"{nameof(NntpServerOptions.BindAddress)} '{bindAddress}' is not a valid IPv4 bind address.");
        }

        /// <summary>
        /// Validates <see cref="NntpServerOptions.BindAddress6"/> for startup binding.
        /// </summary>
        /// <param name="bindAddress6">Configured IPv6 bind address.</param>
        /// <returns>
        /// A <see cref="ValidateOptionsResult"/> failure when the value is non-empty but not a valid IPv6 bind target;
        /// otherwise <see langword="null"/>.
        /// </returns>
        public static ValidateOptionsResult? ValidateBindAddress6(string? bindAddress6)
        {
            if (string.IsNullOrWhiteSpace(bindAddress6))
            {
                return null;
            }

            if (TryResolveIpv6BindAddress(bindAddress6, out IPAddress? ip) && ip is not null)
            {
                return null;
            }

            return ValidateOptionsResult.Fail(
                $"{nameof(NntpServerOptions.BindAddress6)} '{bindAddress6}' is not a valid IPv6 bind address.");
        }
    }
}
