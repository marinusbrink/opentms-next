using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace OpenTms.Hosting;

/// <summary>
/// The OpenIddict certificate of a deployed environment, handed over as PEM by Secret Manager
/// (docs/designs/gcp-infrastructure.md). Risk class: Critical — authentication: every token is signed and encrypted
/// with it; a half-configured environment must fail loudly instead of falling back to a missing pfx.
/// </summary>
public class OpenIddictCertificatesTests
{
    private static IConfiguration Configuration(string? certificatePem, string? keyPem) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OpenIddictCertificates.CertificatePemKey] = certificatePem,
                [OpenIddictCertificates.KeyPemKey] = keyPem,
            })
            .Build();

    /// <summary>The same shape Terraform produces: RSA 2048, self-signed, digital signature + key encipherment.</summary>
    private static (string CertificatePem, string KeyPem) TerraformLikePair()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=OpenTMS test OpenIddict", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(2));
        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    [Fact]
    public void Neither_setting_means_no_certificate_so_the_host_keeps_loading_openiddict_pfx()
    {
        OpenIddictCertificates.FromConfiguration(Configuration(null, null)).ShouldBeNull();
        OpenIddictCertificates.FromConfiguration(Configuration("", "  ")).ShouldBeNull();
    }

    [Fact]
    public void A_pem_pair_loads_as_a_certificate_with_a_usable_private_key()
    {
        var (certificatePem, keyPem) = TerraformLikePair();

        using var certificate = OpenIddictCertificates.FromConfiguration(Configuration(certificatePem, keyPem));

        certificate.ShouldNotBeNull();
        certificate.HasPrivateKey.ShouldBeTrue();
        certificate.Subject.ShouldBe("CN=OpenTMS test OpenIddict");

        // Sign and decrypt with the loaded key: what OpenIddict does with it for tokens.
        var data = new byte[] { 1, 2, 3, 4 };
        using var privateKey = certificate.GetRSAPrivateKey()!;
        using var publicKey = certificate.GetRSAPublicKey()!;
        var signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
        privateKey.Decrypt(publicKey.Encrypt(data, RSAEncryptionPadding.OaepSHA256), RSAEncryptionPadding.OaepSHA256).ShouldBe(data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_one_of_the_two_settings_is_a_configuration_error(bool certificateOnly)
    {
        var (certificatePem, keyPem) = TerraformLikePair();
        var configuration = certificateOnly ? Configuration(certificatePem, null) : Configuration(null, keyPem);

        var error = Should.Throw<InvalidOperationException>(() => OpenIddictCertificates.FromConfiguration(configuration));

        error.Message.ShouldContain(OpenIddictCertificates.CertificatePemKey);
        error.Message.ShouldContain(OpenIddictCertificates.KeyPemKey);
    }

    [Fact]
    public void A_key_that_does_not_belong_to_the_certificate_is_refused()
    {
        var (certificatePem, _) = TerraformLikePair();
        var (_, otherKeyPem) = TerraformLikePair();

        Should.Throw<CryptographicException>(() => OpenIddictCertificates.FromConfiguration(Configuration(certificatePem, otherKeyPem)));
    }
}
