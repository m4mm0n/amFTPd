/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CertificateHelper.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x516E5D0C
 *  
 *  Description:
 *      Creates a self-signed X.509 certificate asynchronously.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace amFTPd.Security;

/// <summary>
/// Creates a self-signed X.509 certificate asynchronously.
/// </summary>
/// <remarks>The generated certificate is valid for three years from the current date and includes the following
/// properties: <list type="bullet"> <item> <description>Key size: 3072 bits (RSA).</description> </item> <item>
/// <description>Hash algorithm: SHA-256.</description> </item> <item> <description>Key usage: Digital Signature and Key
/// Encipherment.</description> </item> </list> The certificate is exported in PFX format and stored in memory with the
/// specified password.</remarks>
internal static class CertificateHelper
{
    /// <summary>
    /// Creates a self-signed X.509 certificate with the specified subject name and password.
    /// </summary>
    /// <remarks>The generated certificate is valid for three years from the current date and includes the
    /// following extensions: <list type="bullet"> <item> <description>Basic Constraints: Indicates the certificate is
    /// not a Certificate Authority (CA).</description> </item> <item> <description>Subject Key Identifier: Provides a
    /// unique identifier for the certificate's public key.</description> </item> <item> <description>Key Usage:
    /// Specifies the certificate can be used for digital signatures and key encipherment.</description> </item> </list>
    /// The certificate is exported in PFX format and marked as exportable with an ephemeral key set.</remarks>
    /// <param name="subject">The distinguished name (DN) of the certificate subject, in X.500 format.</param>
    /// <param name="pfxPassword">The password used to protect the exported PFX file.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the generated  <see
    /// cref="X509Certificate2"/> object, which includes the self-signed certificate.</returns>
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