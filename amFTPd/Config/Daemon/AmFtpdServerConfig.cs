/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdServerConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xE28DF13C
 *  
 *  Description:
 *      Represents the configuration settings for an FTP server.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Config.Daemon
{
    /// <summary>
    /// Represents the configuration settings for an FTP server.
    /// </summary>
    /// <remarks>This configuration defines the server's binding address, port settings, root directory,
    /// authentication requirements, and other operational parameters. It is used to initialize and configure the
    /// behavior of the FTP server.</remarks>
    /// <param name="BindAddress">The IP address or hostname to which the server will bind. This determines the network interface the server
    /// listens on.</param>
    /// <param name="Port">The port number on which the server will listen for incoming connections. Must be a valid TCP port number
    /// (1-65535).</param>
    /// <param name="PassivePortStart">The starting port number for the range of ports used in passive mode data transfers. Must be a valid TCP port
    /// number (1-65535) and less than or equal to <paramref name="PassivePortEnd"/>.</param>
    /// <param name="PassivePortEnd">The ending port number for the range of ports used in passive mode data transfers. Must be a valid TCP port
    /// number (1-65535) and greater than or equal to <paramref name="PassivePortStart"/>.</param>
    /// <param name="RootPath">The root directory path for the FTP server. All file operations will be relative to this directory. Must be a
    /// valid, accessible directory path.</param>
    /// <param name="WelcomeMessage">The message displayed to clients upon successful connection to the server.</param>
    /// <param name="AllowAnonymous"><see langword="true"/> if anonymous access is allowed; otherwise, <see langword="false"/>.</param>
    /// <param name="RequireTlsForAuth"><see langword="true"/> if TLS is required for authentication; otherwise, <see langword="false"/>.</param>
    /// <param name="DataChannelProtectionDefault">The default data channel protection level. Use "C" for clear (unencrypted) data transfers or "P" for protected
    /// (encrypted) data transfers.</param>
    /// <param name="AllowActiveMode"><see langword="true"/> if active mode data transfers are allowed; otherwise, <see langword="false"/>.</param>
    /// <param name="AllowFxp"><see langword="true"/> if File eXchange Protocol (FXP) transfers are allowed; otherwise, <see
    /// langword="false"/>.</param>
    public sealed record AmFtpdServerConfig(
        string BindAddress,
        int Port,
        int PassivePortStart,
        int PassivePortEnd,
        string RootPath,
        string WelcomeMessage,
        bool AllowAnonymous,
        bool RequireTlsForAuth,
        string DataChannelProtectionDefault,   // "C" or "P"
        bool AllowActiveMode,
        bool AllowFxp
    );
}
