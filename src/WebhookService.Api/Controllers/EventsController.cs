using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookService.Api.Data;
using WebhookService.Api.Models;

namespace WebhookService.Api.Controllers;

[ApiController]
[Route("events")]
public class EventsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Publish(PublishEventRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            return BadRequest(new { error = "Type is required, for example payment.captured." });

        var now = DateTime.UtcNow;
        var webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            Type = request.Type.Trim(),
            Payload = request.Payload?.GetRawText() ?? "{}",
            CreatedAt = now
        };

        var activeEndpoints = await db.Endpoints
            .Where(e => e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var deliveries = activeEndpoints.Select(endpointId => new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            EventId = webhookEvent.Id,
            EndpointId = endpointId,
            Status = DeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        }).ToList();

        // One save = one transaction: the event and its deliveries are stored together, or not at all.
        db.Events.Add(webhookEvent);
        db.Deliveries.AddRange(deliveries);
        await db.SaveChangesAsync(ct);

        // 202 Accepted: we have stored it and will deliver it in the background.
        return Accepted(new PublishEventResponse(webhookEvent.Id, deliveries.Count));
    }
}
