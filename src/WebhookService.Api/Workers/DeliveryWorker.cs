using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebhookService.Api.Data;
using WebhookService.Api.Models;

namespace WebhookService.Api.Workers;

public class DeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    private readonly WebhookOptions _opt = options.Value;

    private int[] Delays => _opt.RetryDelaysSeconds.Length > 0
        ? _opt.RetryDelaysSeconds
        : new[] { 10, 60, 300, 1800 };

    private int MaxAttempts => Delays.Length + 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Delivery worker started. Max attempts per message: {Max}", MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Delivery worker batch failed. Will try again.");
            }

            if (processed == 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _opt.PollIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var due = await db.Deliveries
            .Include(d => d.Event)
            .Include(d => d.Endpoint)
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(_opt.BatchSize)
            .ToListAsync(ct);

        foreach (var delivery in due)
        {
            await AttemptAsync(db, delivery, ct);
        }

        return due.Count;
    }

    private async Task AttemptAsync(AppDbContext db, WebhookDelivery d, CancellationToken ct)
    {
        if (!d.Endpoint.IsActive)
        {
            d.Status = DeliveryStatus.Failed;
            d.LastError = "Endpoint is disabled.";
            await db.SaveChangesAsync(ct);
            return;
        }

        d.AttemptCount++;

        try
        {
            var body = BuildBody(d);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var request = new HttpRequestMessage(HttpMethod.Post, d.Endpoint.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Webhook-Id", d.Id.ToString());
            request.Headers.Add("X-Webhook-Event", d.Event.Type);
            request.Headers.Add("X-Webhook-Timestamp", timestamp.ToString());
            request.Headers.Add("X-Webhook-Signature", WebhookSigner.Sign(d.Endpoint.Secret, timestamp, body));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _opt.TimeoutSeconds)));

            var client = httpClientFactory.CreateClient("webhooks");
            using var response = await client.SendAsync(request, timeout.Token);

            d.LastStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                d.Status = DeliveryStatus.Succeeded;
                d.DeliveredAt = DateTime.UtcNow;
                d.LastError = null;
            }
            else
            {
                RecordFailure(d, $"Receiver answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // The app is shutting down. Nothing is saved, so this attempt is retried later.
        }
        catch (Exception ex)
        {
            d.LastStatusCode = null;
            RecordFailure(d, ex is OperationCanceledException ? "Timed out." : ex.Message);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Delivery {Id} attempt {Attempt}/{Max} to {Url}: {Status} (HTTP {Code}) {Error}",
            d.Id, d.AttemptCount, MaxAttempts, d.Endpoint.Url, d.Status, d.LastStatusCode, d.LastError);
    }

    private void RecordFailure(WebhookDelivery d, string reason)
    {
        d.LastError = reason.Length > 500 ? reason[..500] : reason;

        if (d.AttemptCount >= MaxAttempts)
        {
            d.Status = DeliveryStatus.Failed; // Gave up. Kept in the table so a person can look at it.
        }
        else
        {
            d.NextAttemptAt = DateTime.UtcNow.AddSeconds(Delays[d.AttemptCount - 1]);
        }
    }

    private static string BuildBody(WebhookDelivery d)
    {
        using var payload = JsonDocument.Parse(d.Event.Payload);
        return JsonSerializer.Serialize(new
        {
            id = d.EventId,
            type = d.Event.Type,
            createdAt = d.Event.CreatedAt,
            data = payload.RootElement
        });
    }
}
