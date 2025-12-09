/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IdentManager.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xD43FF98F
 *  
 *  Description:
 *      Manages IDENT protocol queries and applies user and group policies based on the results.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using amFTPd.Config.Ident;

namespace amFTPd.Core.Ident;

/// <summary>
/// Manages IDENT protocol queries and applies user and group policies based on the results.
/// </summary>
/// <remarks>The <see cref="IdentManager"/> class is responsible for performing IDENT lookups, caching
/// results (if enabled), and enforcing various policies such as strict user matching, group mapping, TLS binding,
/// and reverse DNS checks. It operates based on the configuration provided via the <see cref="IdentConfig"/>
/// object.</remarks>
public sealed class IdentManager
{
    private readonly IdentConfig _config;
    private readonly IdentClient _client = new();
    private readonly IdentCache? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentManager"/> class with the specified configuration.
    /// </summary>
    /// <remarks>If the <paramref name="config"/> specifies the <see cref="IdentMode.Caching"/> flag,
    /// a caching mechanism is initialized with a time-to-live (TTL) duration defined by <see
    /// cref="IdentConfig.CacheTtlSeconds"/>.</remarks>
    /// <param name="config">The configuration settings for the <see cref="IdentManager"/>, including operational modes and caching
    /// options.</param>
    public IdentManager(IdentConfig config)
    {
        _config = config;
        if (_config.Modes.HasFlag(IdentMode.Caching))
            _cache = new IdentCache(TimeSpan.FromSeconds(_config.CacheTtlSeconds));
    }
    /// <summary>
    /// Gets the configuration settings for the current instance.
    /// </summary>
    public IdentConfig Config => _config;
    /// <summary>
    /// Queries the identity of a remote client, applies the configured policies, and returns the result.
    /// </summary>
    /// <remarks>This method performs the following steps: <list type="bullet">
    /// <item><description>Validates the configured identification modes.</description></item>
    /// <item><description>Checks the local and remote endpoints of the control connection.</description></item>
    /// <item><description>Uses a cache, if available, to retrieve or store the identity
    /// result.</description></item> <item><description>Applies the configured policies to the identity
    /// result.</description></item> </list> The method ensures that the configured policies are applied to the
    /// identity result, which may include group membership checks, logging, and other actions based on the provided
    /// delegates.</remarks>
    /// <param name="controlClient">The <see cref="TcpClient"/> representing the control connection to the remote client.</param>
    /// <param name="ftpUserName">The FTP username associated with the client, or <see langword="null"/> if unavailable.</param>
    /// <param name="clientCert">The client's TLS certificate, or <see langword="null"/> if no certificate is provided.</param>
    /// <param name="isUserInGroup">A delegate that determines whether the specified user belongs to a group.</param>
    /// <param name="addUserToGroup">A delegate that adds the specified user to a group.</param>
    /// <param name="logInfo">A delegate for logging informational messages.</param>
    /// <param name="logWarn">A delegate for logging warning messages.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to cancel the operation.</param>
    /// <returns>An <see cref="IdentResult"/> representing the outcome of the identity query and policy application. Returns
    /// <see cref="IdentResult.Failed"/> if the operation cannot be completed.</returns>
    public async Task<IdentResult> QueryAndApplyPolicyAsync(
        TcpClient controlClient,
        string? ftpUserName,
        X509Certificate2? clientCert,
        Func<string, bool> isUserInGroup,
        Action<string> addUserToGroup,
        Action<string> logInfo,
        Action<string> logWarn,
        CancellationToken ct)
    {
        if (!_config.Modes.HasFlag(IdentMode.Standard) &&
            !_config.Modes.HasFlag(IdentMode.LoggingOnly) &&
            !_config.Modes.HasFlag(IdentMode.StrictUserMatch) &&
            !_config.Modes.HasFlag(IdentMode.GroupMapping) &&
            !_config.Modes.HasFlag(IdentMode.ReverseDnsCheck) &&
            !_config.Modes.HasFlag(IdentMode.TlsBinding))
            return IdentResult.Failed;

        if (controlClient.Client.LocalEndPoint is not IPEndPoint local ||
            controlClient.Client.RemoteEndPoint is not IPEndPoint remote)
            return IdentResult.Failed;

        if (_cache is not null && _cache.TryGet(remote.Address, out var cached))
        {
            ApplyPolicies(cached, ftpUserName, clientCert, isUserInGroup, addUserToGroup, logInfo, logWarn);
            return cached;
        }

        var result = await _client.QueryAsync(remote, local, _config.TimeoutMs, ct).ConfigureAwait(false);

        if (_cache is not null && result.Success)
            _cache.Set(remote.Address, result);

        ApplyPolicies(result, ftpUserName, clientCert, isUserInGroup, addUserToGroup, logInfo, logWarn);

        return result;
    }

    private void ApplyPolicies(
        IdentResult result,
        string? ftpUserName,
        X509Certificate2? clientCert,
        Func<string, bool> isUserInGroup,
        Action<string> addUserToGroup,
        Action<string> logInfo,
        Action<string> logWarn)
    {
        var modes = _config.Modes;

        if (modes.HasFlag(IdentMode.LoggingOnly))
        {
            var msg = result.Success
                ? $"IDENT: user '{result.Username}', os '{result.OpsSystem}', raw '{result.RawResponse}'"
                : "IDENT: lookup failed.";
            logInfo(msg);
        }

        if (!result.Success)
            return;

        var identUser = result.Username;

        if (modes.HasFlag(IdentMode.GroupMapping) && !string.IsNullOrEmpty(identUser))
        {
            foreach (var mapping in _config.GroupMappings.Where(m =>
                         string.Equals(m.IdentUser, identUser, StringComparison.OrdinalIgnoreCase)))
            {
                if (!isUserInGroup(mapping.GroupName))
                {
                    addUserToGroup(mapping.GroupName);
                    logInfo($"IDENT: user '{ftpUserName}' mapped to group '{mapping.GroupName}' via IDENT user '{identUser}'.");
                }
            }
        }

        if (modes.HasFlag(IdentMode.StrictUserMatch) && !string.IsNullOrEmpty(ftpUserName))
        {
            if (!string.Equals(ftpUserName, identUser, StringComparison.OrdinalIgnoreCase))
            {
                var msg = $"IDENT strict mode failed: FTP user '{ftpUserName}' != IDENT user '{identUser}'.";
                if (_config.DenyOnStrictMismatch)
                    throw new IdentPolicyException(msg);
                logWarn(msg);
            }
        }

        if (modes.HasFlag(IdentMode.TlsBinding) && clientCert is not null && !string.IsNullOrEmpty(identUser))
        {
            var cn = clientCert.GetNameInfo(X509NameType.SimpleName, false);
            if (!string.Equals(cn, identUser, StringComparison.OrdinalIgnoreCase))
            {
                var msg = $"IDENT TLS binding failed: IDENT user '{identUser}' != certificate CN '{cn}'.";
                if (_config.DenyOnTlsBindingMismatch)
                    throw new IdentPolicyException(msg);
                logWarn(msg);
            }
        }

        if (modes.HasFlag(IdentMode.ReverseDnsCheck))
        {
            // Keep simple: log-only stub; you can extend to real PTR+policy.
            // Real implementation would do a PTR lookup and compare to identUser.
            logInfo("IDENT: ReverseDnsCheck mode is enabled (PTR check not fully implemented here).");
        }
    }
}