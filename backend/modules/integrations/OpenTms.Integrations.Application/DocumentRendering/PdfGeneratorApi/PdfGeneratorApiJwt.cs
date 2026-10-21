using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The JWT pdfgeneratorapi.com expects on every request (design 50): HS256, <c>iss</c> = the API key, <c>sub</c> = the
/// workspace, <c>exp</c> = now + 60 seconds; header, payload and signature Base64Url-encoded as JWT requires (OpenTMS
/// used standard Base64 for the signature and 15 minutes). A token is never logged.
/// </summary>
public static class PdfGeneratorApiJwt
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private static readonly byte[] Header = """{"alg":"HS256","typ":"JWT"}"""u8.ToArray();

    public static string Create(string key, string secret, string workspace, DateTimeOffset now)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = key,
            ["sub"] = workspace,
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds()
        });

        var signingInput = Base64Url.EncodeToString(Header) + "." + Base64Url.EncodeToString(payload);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }
}
