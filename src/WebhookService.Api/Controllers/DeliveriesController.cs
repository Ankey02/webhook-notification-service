using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookService.Api.Data;
using WebhookService.Api.Models;

namespace WebhookService.Api.Controllers;

[ApiController]
[Route("deliveries")]
public class DeliveriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? status, int limit = 50, CancellationToken ct = default)
    {
        var query = db.Deliveries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DeliveryStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new { error = "Status must be Pending, Succeeded or Failed." });
            query = query.Where(d => d.Status == parsed);
        }

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(d => new DeliveryResponse(
                d.Id, d.EventId, d.Event.Type, d.EndpointId, d.Endpoint.Url,
                d.Status.ToString(), d.AttemptCount, d.NextAttemptAt,
                d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DeliveryResponse(
                d.Id, d.EventId, d.Event.Type, d.EndpointId, d.Endpoint.Url,
                d.Status.ToString(), d.AttemptCount, d.NextAttemptAt,
                d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt))
            .FirstOrDefaultAsync(ct);

        return item is null ? NotFound() : Ok(item);
    }
}
