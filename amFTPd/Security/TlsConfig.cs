/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           TlsConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 21:09:20
 *  CRC32:          0xD92F4DF7
 *  
 *  Description:
 *      Represents the configuration for Transport Layer Security (TLS), including the server certificate and supported SSL/T...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */



using amFTPd.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
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
        /// When true, clear-text data channels (PROT C) are refused whenever the control
        /// connection is protected with TLS. This is a generic knob honoured by the
        /// data-connection layer.
        /// </summary>
        public bool RefuseClearDataOnSecureControl { get; init; } = false;

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
            string pfxPath,
            string? pfxPassword,
            string subjectName,
            IFtpLogger logger)
        {
            // Normalize password: null / empty / whitespace => null (no password)
            var pwd = string.IsNullOrWhiteSpace(pfxPassword) ? null : pfxPassword;

            // Use *user* keystore so we don't need admin rights, and persist the key
            const X509KeyStorageFlags Flags =
                X509KeyStorageFlags.UserKeySet |
                X509KeyStorageFlags.Exportable |
                X509KeyStorageFlags.PersistKeySet;

            // 1) Try load existing PFX
            if (File.Exists(pfxPath))
            {
                try
                {
                    logger.Log(FtpLogLevel.Info, $"Loading certificate: {pfxPath}");

                    var raw = await File.ReadAllBytesAsync(pfxPath).ConfigureAwait(false);

#pragma warning disable SYSLIB0057
                    var cert = new X509Certificate2(raw, pwd, Flags);
#pragma warning restore SYSLIB0057

                    if (!cert.HasPrivateKey)
                    {
                        throw new CryptographicException(
                            $"Loaded certificate '{pfxPath}' does not have a private key.");
                    }

                    return new TlsConfig(cert);
                }
                catch (CryptographicException ex)
                {
                    logger.Log(
                        FtpLogLevel.Error,
                        $"Failed to load existing certificate '{pfxPath}': {ex.Message}. " +
                        "Deleting and regenerating a new self-signed certificate..."
                    );

                    try { File.Delete(pfxPath); } catch { /* ignore */ }
                }
            }

            // 2) Generate new self-signed RSA cert (with persistent user key)
            logger.Log(FtpLogLevel.Info, "Generating self-signed certificate (RSA)...");
            var certNew = CreateSelfSignedRsaCertificate(subjectName, pwd, Flags);

            // 3) Persist PFX for future runs
            try
            {
                var pfxBytes = certNew.Export(X509ContentType.Pkcs12, pwd);
                Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);
                await File.WriteAllBytesAsync(pfxPath, pfxBytes).ConfigureAwait(false);
                logger.Log(FtpLogLevel.Info, $"Saved new certificate: {pfxPath}");
            }
            catch (Exception ex)
            {
                logger.Log(
                    FtpLogLevel.Warn,
                    $"Failed to save TLS certificate to '{pfxPath}': {ex.Message}"
                );
            }

            return new TlsConfig(certNew);
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
        /// <summary>
        /// Returns <see langword="true"/> if the given protocol is considered strong enough
        /// compared to the configured minimum.
        /// </summary>
        /// <param name="protocol">The negotiated protocol (e.g. from <see cref="SslStream.SslProtocol"/>).</param>
        /// <param name="minimum">The minimum acceptable protocol.</param>
        public bool IsProtocolStrongEnough(SslProtocols protocol, SslProtocols minimum)
        {
            if (minimum == SslProtocols.None)
                return true;

            var actualRank = GetProtocolRank(protocol);
            var requiredRank = GetProtocolRank(minimum);

            if (requiredRank == 0)
                return true; // "no opinion" about the minimum

            return actualRank >= requiredRank && actualRank != 0;
        }

        private static int GetProtocolRank(SslProtocols protocol)
            => protocol switch
            {
                //SslProtocols.Tls => 1, // TLS 1.0 is considered weak
                //SslProtocols.Tls11 => 2, // TLS 1.1 is considered weak
                SslProtocols.Tls12 => 3,
                SslProtocols.Tls13 => 4,
                _ => 0
            };

        private static X509Certificate2 CreateSelfSignedRsaCertificate(
            string subjectName,
            string? pfxPassword,
            X509KeyStorageFlags flags)
        {
            using var rsa = RSA.Create(4096);

            var req = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            // 1) Self-signed cert with in-memory key (may be ephemeral)
            using var temp = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(5));

            // 2) Export to PFX using the same password we always use
            var pfxBytes = temp.Export(X509ContentType.Pkcs12, pfxPassword);

            // 3) Re-import into user store with persistent key so Schannel can use it
#pragma warning disable SYSLIB0057
            var cert = new X509Certificate2(pfxBytes, pfxPassword, flags);
#pragma warning restore SYSLIB0057

            if (!cert.HasPrivateKey)
            {
                throw new CryptographicException("Generated TLS certificate has no private key.");
            }

            return cert;
        }
    }
}
