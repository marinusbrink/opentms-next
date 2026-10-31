using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace OpenTms.Hosting;

/* The OpenIddict signing and encryption certificate of a deployed environment (docs/designs/gcp-infrastructure.md).
 * Terraform generates the key and certificate and Secret Manager hands them over as PEM in
 * AuthServer:SigningCertificatePem and AuthServer:SigningKeyPem, so no openiddict.pfx file is passed around.
 * Without both settings the host keeps loading openiddict.pfx as before.
 */
public static class OpenIddictCertificates
{
    public const string CertificatePemKey = "AuthServer:SigningCertificatePem";
    public const string KeyPemKey = "AuthServer:SigningKeyPem";

    /// <summary>The certificate from the PEM settings, or null when neither is set.</summary>
    public static X509Certificate2? FromConfiguration(IConfiguration configuration)
    {
        var certificatePem = configuration[CertificatePemKey];
        var keyPem = configuration[KeyPemKey];

        if (string.IsNullOrWhiteSpace(certificatePem) && string.IsNullOrWhiteSpace(keyPem))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(certificatePem) || string.IsNullOrWhiteSpace(keyPem))
        {
            throw new InvalidOperationException($"Set both {CertificatePemKey} and {KeyPemKey}, or neither.");
        }

        /* CreateFromPem gives the certificate an ephemeral key; a PKCS#12 round trip gives it a regular one,
         * which every platform accepts for signing and decryption. */
        using var fromPem = X509Certificate2.CreateFromPem(certificatePem, keyPem);
        return X509CertificateLoader.LoadPkcs12(fromPem.Export(X509ContentType.Pkcs12), password: null);
    }
}
