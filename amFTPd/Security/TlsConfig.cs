/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           TlsConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-13 22:05:34
 *  CRC32:          0x9FF3ADA6
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
            string pfxPassword,
            string subjectName,
            IFtpLogger logger)
        {
            const X509KeyStorageFlags Flags =
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.Exportable;

            // 1) Try load existing PFX
            if (File.Exists(pfxPath))
            {
                try
                {
                    logger.Log(FtpLogLevel.Info, $"Loading certificate: {pfxPath}");

                    var cert = X509CertificateLoader.LoadPkcs12FromFile(
                        pfxPath,
                        pfxPassword,
                        Flags
                    );
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

            // 2) Generate new self-signed RSA cert
            logger.Log(FtpLogLevel.Info, "Generating self-signed certificate (RSA)...");
            var certNew = CreateSelfSignedRsaCertificate(subjectName, pfxPassword);

            // 3) Try to persist it for next runs
            try
            {
                var pfxBytes = certNew.Export(X509ContentType.Pfx, pfxPassword);
                await File.WriteAllBytesAsync(pfxPath, pfxBytes);
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
            string pfxPassword)
        {
            using var rsa = RSA.Create(2048);

            var dn = new X500DistinguishedName($"CN={subjectName}");

            var req = new CertificateRequest(
                dn,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Not a CA
            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            // Key usage: digital signature + key encipherment
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            // Subject key identifier
            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(
                    req.PublicKey,
                    critical: false));

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = notBefore.AddYears(5);

            using var generated = req.CreateSelfSigned(notBefore, notAfter);

            // Export as PFX and re-import with *user* key store, persisted
            var pfxBytes = generated.Export(X509ContentType.Pfx, pfxPassword);

            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                pfxPassword,
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable
            );
        }
    }
}
