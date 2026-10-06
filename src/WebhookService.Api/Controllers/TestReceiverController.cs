using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace WebhookService.Api.Controllers;

public record ReceivedMessage(
    DateTime At, string DeliveryId, int Attempt, int Returned,
    string? EventType, string? Signature, string Body);

// A fake merchant server for trying the service out. Development only.
[ApiController]
[Route("test-receiver")]
public class TestReceiverController(IWebHostEnvironment env) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new();
    private static readonly ConcurrentQueue<ReceivedMessage> Received = new();

    // Always answers 200.
    [HttpPost("ok")]
    public Task<IActionResult> Ok200(CancellationToken ct) => Handle(0, ct);

    // Answers 500 for the first N attempts of each message, then 200.
    [HttpPost("fail-first/{failures:int}")]
    public Task<IActionResult> FailFirst(int failures, CancellationToken ct) => Handle(failures, ct);

    // Shows what this fake server has received, newest first.
    [HttpGet("received")]
    public IActionResult List() =>
        env.IsDevelopment() ? Ok(Received.OrderByDescending(m => m.At)) : NotFound();

    private async Task<IActionResult> Handle(int failures, CancellationToken ct)
    {
        if (!env.IsDevelopment()) return NotFound();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var id = Request.Headers["X-Webhook-Id"].ToString();
        var attempt = Attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
        var returned = attempt <= failures ? 500 : 200;

        Received.Enqueue(new ReceivedMessage(
            DateTime.UtcNow, id, attempt, returned,
            Request.Headers["X-Webhook-Event"].ToString(),
            Request.Headers["X-Webhook-Signature"].ToString(),
            body));
        while (Received.Count > 50 && Received.TryDequeue(out _)) { }

        return returned == 200
            ? Ok(new { received = true })
            : StatusCode(500, new { error = "Simulated failure" });
    }
}
