using amFTPd.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace amFTPd.Security
{
    public sealed class TlsConfig
    {
        public X509Certificate2 Certificate { get; }
        public SslProtocols Protocols { get; } = SslProtocols.Tls13 | SslProtocols.Tls12;

        public TlsConfig(X509Certificate2 cert) => Certificate = cert;

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
