using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace amFTPd.Security;

internal static class CertificateHelper
{
    public static Task<X509Certificate2> CreateSelfSignedAsync(string subject, string pfxPassword)
    {
        var dn = new X500DistinguishedName(subject);
        using var rsa = RSA.Create(3072);
        var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddYears(3);

        var cert = req.CreateSelfSigned(notBefore, notAfter);
        return Task.FromResult(new X509Certificate2(cert.Export(X509ContentType.Pfx, pfxPassword), pfxPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet));
    }
}