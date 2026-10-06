using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace WebhookService.Tests;

public record RecordedCall(
    Uri Url, string DeliveryId, int Attempt, string? Signature, string? Timestamp, string Body);

// Stands in for the merchant's server. The URL path decides how it behaves.
public class FakeReceiverHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, int> _attempts = new();
    public ConcurrentQueue<RecordedCall> Calls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = request.Headers.GetValues("X-Webhook-Id").Single();
        var attempt = _attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);

        Calls.Enqueue(new RecordedCall(
            request.RequestUri!, id, attempt,
            request.Headers.TryGetValues("X-Webhook-Signature", out var s) ? s.Single() : null,
            request.Headers.TryGetValues("X-Webhook-Timestamp", out var t) ? t.Single() : null,
            body));

        var status = request.RequestUri!.AbsolutePath switch
        {
            "/always-fail" => HttpStatusCode.InternalServerError,
            "/fail-first-2" => attempt <= 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            _ => HttpStatusCode.OK
        };

        return new HttpResponseMessage(status);
    }

    // The HTTP client factory may try to dispose this. Keep it alive for the whole test run.
    protected override void Dispose(bool disposing) { }
}

public class WebhookApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16").Build();

    public FakeReceiverHandler Receiver { get; } = new();

    async Task IAsyncLifetime.InitializeAsync() => await _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());

        // Retry quickly in tests: 1 second between each of the 5 attempts.
        for (var i = 0; i < 4; i++)
            builder.UseSetting($"Webhooks:RetryDelaysSeconds:{i}", "1");
        builder.UseSetting("Webhooks:PollIntervalSeconds", "1");

        // Send webhooks to the fake merchant server instead of the internet.
        builder.ConfigureTestServices(services =>
            services.AddHttpClient("webhooks").ConfigurePrimaryHttpMessageHandler(() => Receiver));
    }
}
