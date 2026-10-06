using System.Security.Cryptography;
using System.Text;

namespace WebhookService.Api.Workers;

public static class WebhookSigner
{
    // Signature = HMAC-SHA256 of "timestamp.body", using the endpoint's secret.
    // The receiver recomputes it and compares, so it knows the message is genuine.
    public static string Sign(string secret, long timestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }
}
