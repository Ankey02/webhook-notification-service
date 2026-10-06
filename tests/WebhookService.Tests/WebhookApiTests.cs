using System.Net;
using System.Net.Http.Json;
using WebhookService.Api.Controllers;
using WebhookService.Api.Workers;

namespace WebhookService.Tests;

public class WebhookApiTests(WebhookApiFactory factory) : IClassFixture<WebhookApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Before every test: switch off all endpoints left over from earlier tests.
    public async Task InitializeAsync()
    {
        var endpoints = await _client.GetFromJsonAsync<List<EndpointResponse>>("/endpoints");
        foreach (var endpoint in endpoints!.Where(e => e.IsActive))
            await _client.DeleteAsync($"/endpoints/{endpoint.Id}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Delivers_a_signed_message_to_the_endpoint()
    {
        var (_, secret) = await CreateEndpoint("/ok");

        var published = await Publish("payment.captured");
        Assert.Equal(1, published.DeliveriesCreated);

        var delivery = await WaitUntilDone(published.EventId);
        Assert.Equal("Succeeded", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);

        // The receiver can verify the message really came from us.
        var call = Assert.Single(factory.Receiver.Calls, c => c.DeliveryId == delivery.Id.ToString());
        var expected = WebhookSigner.Sign(secret, long.Parse(call.Timestamp!), call.Body);
        Assert.Equal(expected, call.Signature);
        Assert.Contains("\"type\":\"payment.captured\"", call.Body);
    }

    [Fact]
    public async Task Retries_a_failing_endpoint_until_it_succeeds()
    {
        await CreateEndpoint("/fail-first-2");

        var published = await Publish("payment.captured");
        var delivery = await WaitUntilDone(published.EventId);

        Assert.Equal("Succeeded", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
        Assert.Equal(3, factory.Receiver.Calls.Count(c => c.DeliveryId == delivery.Id.ToString()));
    }

    [Fact]
    public async Task Gives_up_after_the_last_attempt()
    {
        await CreateEndpoint("/always-fail");

        var published = await Publish("payment.failed");
        var delivery = await WaitUntilDone(published.EventId);

        Assert.Equal("Failed", delivery.Status);
        Assert.Equal(5, delivery.AttemptCount);
        Assert.Equal(500, delivery.LastStatusCode);
    }

    [Fact]
    public async Task Publishing_with_no_active_endpoints_creates_no_deliveries()
    {
        var published = await Publish("payment.captured");

        Assert.Equal(0, published.DeliveriesCreated);
    }

    [Fact]
    public async Task A_disabled_endpoint_gets_no_messages()
    {
        var (id, _) = await CreateEndpoint("/ok");

        var disable = await _client.DeleteAsync($"/endpoints/{id}");
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var published = await Publish("payment.captured");
        Assert.Equal(0, published.DeliveriesCreated);
    }

    [Fact]
    public async Task Rejects_an_invalid_url()
    {
        var response = await _client.PostAsJsonAsync("/endpoints", new { url = "not-a-url" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_an_event_without_a_type()
    {
        var response = await _client.PostAsJsonAsync("/events", new { type = "", payload = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers ----

    private async Task<(Guid Id, string Secret)> CreateEndpoint(string path)
    {
        var response = await _client.PostAsJsonAsync("/endpoints", new { url = $"http://receiver.test{path}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreatedEndpointResponse>();
        return (created!.Id, created.Secret);
    }

    private async Task<PublishEventResponse> Publish(string type)
    {
        var response = await _client.PostAsJsonAsync("/events",
            new { type, payload = new { paymentId = "pay_1", amount = 2500 } });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PublishEventResponse>())!;
    }

    // Checks the deliveries list until the message is no longer Pending.
    private async Task<DeliveryResponse> WaitUntilDone(Guid eventId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            var list = await _client.GetFromJsonAsync<List<DeliveryResponse>>("/deliveries?limit=200");
            var delivery = list!.FirstOrDefault(d => d.EventId == eventId);
            if (delivery is not null && delivery.Status != "Pending") return delivery;

            await Task.Delay(250);
        }

        throw new TimeoutException("The delivery did not finish in time.");
    }
}
