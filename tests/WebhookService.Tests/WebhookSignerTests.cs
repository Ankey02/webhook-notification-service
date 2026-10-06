using WebhookService.Api.Workers;

namespace WebhookService.Tests;

public class WebhookSignerTests
{
    [Fact]
    public void Produces_the_expected_signature_for_a_known_input()
    {
        var signature = WebhookSigner.Sign("whsec_test", 1700000000, "{\"id\":\"1\"}");

        Assert.Equal("sha256=11bf4466ea17c3df3fd743af0b435368e16b7a05eb8eced85e8c4670767bdec5", signature);
    }

    [Fact]
    public void Signature_changes_when_the_secret_changes()
    {
        var a = WebhookSigner.Sign("whsec_one", 1700000000, "{}");
        var b = WebhookSigner.Sign("whsec_two", 1700000000, "{}");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Signature_changes_when_the_body_or_timestamp_changes()
    {
        var original = WebhookSigner.Sign("whsec_one", 1700000000, "{\"amount\":100}");
        var tamperedBody = WebhookSigner.Sign("whsec_one", 1700000000, "{\"amount\":999}");
        var otherTime = WebhookSigner.Sign("whsec_one", 1700000001, "{\"amount\":100}");

        Assert.NotEqual(original, tamperedBody);
        Assert.NotEqual(original, otherTime);
    }
}
