using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookService.Api.Data;
using WebhookService.Api.Models;

namespace WebhookService.Api.Controllers;

[ApiController]
[Route("endpoints")]
public class EndpointsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateEndpointRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { error = "Url must be a valid http or https address." });
        }

        var secret = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Url = request.Url,
            Secret = secret,
            CreatedAt = DateTime.UtcNow
        };

        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);

        return Created($"/endpoints/{endpoint.Id}",
            new CreatedEndpointResponse(endpoint.Id, endpoint.Url, endpoint.Secret, endpoint.CreatedAt));
    }

    [HttpGet]
    public async Task<IEnumerable<EndpointResponse>> List(CancellationToken ct) =>
        await db.Endpoints.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EndpointResponse(e.Id, e.Url, e.IsActive, e.CreatedAt))
            .ToListAsync(ct);

    // Turns the endpoint off. Old deliveries are kept for the record.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var endpoint = await db.Endpoints.FindAsync([id], ct);
        if (endpoint is null) return NotFound();

        endpoint.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
