/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IrcConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:52:31
 *  Last Modified:  2025-12-14 18:04:36
 *  CRC32:          0xDA57AA5C
 *  
 *  Description:
 *      Gets the nickname associated with the current instance.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Config.Irc
{
    /// <summary>Configuration for the IRC announcer.</summary>
    public sealed class IrcConfig
    {
        /// <summary>
        /// Gets the hostname or IP address of the IRC server to connect to.
        /// </summary>
        public string Server { get; init; } = "irc.example.net";
        /// <summary>
        /// Gets the network port number used for the connection.
        /// </summary>
        public int Port { get; init; } = 7000;
        /// <summary>
        /// Gets a value indicating whether Transport Layer Security (TLS) is used for the connection.
        /// </summary>
        public bool UseTls { get; init; } = true;
        /// <summary>
        /// Gets a value indicating whether the connection should use the STARTTLS command to upgrade to a secure TLS
        /// connection.
        /// </summary>
        /// <remarks>When set to <see langword="true"/>, the client will attempt to initiate a secure
        /// connection using STARTTLS if the server supports it. If set to <see langword="false"/>, the connection will
        /// remain unencrypted unless another security mechanism is used.</remarks>
        public bool UseStartTls { get; init; } = false;
        /// <summary>
        /// Gets the TLS server name to use when establishing a secure connection.
        /// </summary>
        /// <remarks>This value is typically used for Server Name Indication (SNI) during the TLS
        /// handshake. If not set, the default server name may be used, which can affect certificate validation and
        /// server selection.</remarks>
        public string? TlsServerName { get; init; }
        /// <summary>
        /// Gets a value indicating whether TLS connections are allowed to accept invalid or untrusted certificates.
        /// </summary>
        /// <remarks>Enabling this property may expose the application to security risks by allowing
        /// connections to servers with invalid, expired, or self-signed certificates. Use only in trusted environments,
        /// such as development or testing.</remarks>
        public bool TlsAllowInvalidCerts { get; init; } = true;
        /// <summary>
        /// Gets the password used to authenticate with the server.
        /// </summary>
        public string? ServerPassword { get; init; }
        /// <summary>
        /// Gets the nickname associated with the current instance.
        /// </summary>
        public string Nick { get; init; } = "amFTPd-bot";
        /// <summary>
        /// Gets the user name associated with the current instance.
        /// </summary>
        public string User { get; init; } = "amftpd-bot";
        /// <summary>
        /// Gets the real name or description associated with the announcer.
        /// </summary>
        public string RealName { get; init; } = "amFTPd IRC announcer";
        /// <summary>
        /// Gets the IRC channels to which the client will connect. Separate multiple channels with spaces, commas, or semicolons.
        /// </summary>
        public string Channels { get; init; } = "#amftpd";
        /// <summary>
        /// Gets a value indicating whether the feature is enabled.
        /// </summary>
        public bool Enabled { get; init; } = false;
        /// <summary>
        /// Enable FiSH (Blowfish) encryption. If enabled, any target (channel or nick)
        /// found in FishKeys will have messages sent as +OK &lt;blowcrypt-base64&gt;.
        /// </summary>
        public bool FishEnabled { get; init; } = false;
        /// <summary>
        /// FiSH keys by target ("#chan", "nick").
        /// </summary>
        public IDictionary<string, string> FishKeys { get; init; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Optional AMScript file (or script ID) used for custom IRC logic
        /// such as CAP/SASL, extra JOINs, etc.
        /// This is just metadata; wiring to AMScript happens outside.
        /// </summary>
        public string? ScriptFile { get; init; }
        /// <summary>
        /// If true, the AMScript hook (if provided) will be used.
        /// </summary>
        public bool ScriptEnabled { get; init; } = false;
        /// <summary>
        /// Format template for PRE announces. Tokens: {release}, {section}, {user}, {mb}.
        /// </summary>
        public string? PreFormat { get; init; }
        /// <summary>
        /// Format template for NUKE announces. Tokens: {release}, {section}, {user}, {reason}, {mult}.
        /// </summary>
        public string? NukeFormat { get; init; }
        /// <summary>
        /// Format template for UNNUKE announces.
        /// </summary>
        public string? UnnukeFormat { get; init; }
        /// <summary>
        /// Format template for race-complete announces.
        /// </summary>
        public string? RaceCompleteFormat { get; init; }
        /// <summary>
        /// Format template for upload announces. Tokens: {release}, {section}, {user}, {mb}.
        /// </summary>
        public string? UploadFormat { get; init; }
        /// <summary>
        /// Format template for generic zipscript status announces.
        /// </summary>
        public string? ZipscriptFormat { get; init; }
        /// <summary>
        /// Retrieves a list of channel names by splitting the <see cref="Channels"/> string.
        /// </summary>
        /// <remarks>The <see cref="Channels"/> string is split using the delimiters space (' '), comma
        /// (','), and semicolon (';'). Empty or whitespace-only entries are removed from the result. If <see
        /// cref="Channels"/> is null, empty, or consists only of whitespace, an empty array is returned.</remarks>
        /// <returns>An array of strings containing the channel names. Returns an empty array if no valid channels are found.</returns>
        public string[] GetChannelList()
        {
            if (string.IsNullOrWhiteSpace(Channels))
                return [];

            return Channels
                .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
