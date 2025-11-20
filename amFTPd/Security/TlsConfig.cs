/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace amFTPd.Security
{
    /// <summary>
    /// Represents the configuration for Transport Layer Security (TLS), including the server certificate and supported
    /// SSL/TLS protocols.
    /// </summary>
    /// <remarks>This class provides functionality to configure TLS settings for secure communication. It
    /// supports loading an existing certificate from a file or generating a self-signed certificate if none exists. The
    /// default supported protocols are TLS 1.2 and TLS 1.3.</remarks>
    public sealed class TlsConfig
    {
        /// <summary>
        /// Gets the X.509 certificate associated with the current instance.
        /// </summary>
        public X509Certificate2 Certificate { get; }
        /// <summary>
        /// Gets the SSL/TLS protocols supported by the application.
        /// </summary>
        /// <remarks>Use this property to determine or configure the SSL/TLS protocols that the
        /// application supports  for secure communication. Ensure that the selected protocols align with security best
        /// practices  and compliance requirements.</remarks>
        public SslProtocols Protocols { get; } = SslProtocols.Tls13 | SslProtocols.Tls12;
        /// <summary>
        /// Initializes a new instance of the <see cref="TlsConfig"/> class with the specified TLS certificate.
        /// </summary>
        /// <param name="cert">The <see cref="X509Certificate2"/> to be used for TLS configuration. Cannot be <see langword="null"/>.</param>
        public TlsConfig(X509Certificate2 cert) => Certificate = cert;
        /// <summary>
        /// Creates or loads a TLS configuration based on the specified certificate file.
        /// </summary>
        /// <remarks>If the specified PFX file exists, it will be loaded and used to create the TLS
        /// configuration. If the file does not exist, a new self-signed certificate will be generated using the
        /// provided subject name and password, and saved to the specified path.</remarks>
        /// <param name="pfxPath">The file path to the PFX certificate file. If the file does not exist, a new self-signed certificate will be
        /// generated and saved to this path.</param>
        /// <param name="pfxPassword">The password used to access the PFX certificate file or to secure the newly generated certificate.</param>
        /// <param name="subjectName">The subject name to use when generating a new self-signed certificate.</param>
        /// <param name="logger">An instance of <see cref="IFtpLogger"/> used to log informational messages during the operation.</param>
        /// <returns>A <see cref="TlsConfig"/> instance containing the loaded or newly generated certificate.</returns>
        public static async Task<TlsConfig> CreateOrLoadAsync(
            string pfxPath, string pfxPassword, string subjectName, IFtpLogger logger)
        {
            if (File.Exists(pfxPath))
            {
                logger.Log(FtpLogLevel.Info, $"Loading certificate: {pfxPath}");
                return new TlsConfig(new X509Certificate2(pfxPath, pfxPassword));
            }

            logger.Log(FtpLogLevel.Info, "Generating self-signed certificate...");
            var cert = await CertificateHelper.CreateSelfSignedAsync(subjectName, pfxPassword);
            await File.WriteAllBytesAsync(pfxPath, cert.Export(X509ContentType.Pfx, pfxPassword));
            logger.Log(FtpLogLevel.Info, $"Saved new certificate: {pfxPath}");
            return new TlsConfig(cert);
        }
        /// <summary>
        /// Creates and configures an instance of <see cref="SslServerAuthenticationOptions"/>  with predefined server
        /// authentication settings.
        /// </summary>
        /// <remarks>The returned <see cref="SslServerAuthenticationOptions"/> instance is configured with
        /// the  server certificate, SSL protocols, and other settings specified by the current object.  Client
        /// certificates are not required, and certificate revocation checks are disabled.</remarks>
        /// <returns>A configured <see cref="SslServerAuthenticationOptions"/> instance ready for use in server-side  SSL/TLS
        /// authentication.</returns>
        public SslServerAuthenticationOptions CreateServerOptions()
            => new()
            {
                ServerCertificate = Certificate,
                EnabledSslProtocols = Protocols,
                ClientCertificateRequired = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                AllowRenegotiation = false
            };
    }
}
