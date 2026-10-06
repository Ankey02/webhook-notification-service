using System.Text.Json;

namespace WebhookService.Api.Controllers;

public record CreateEndpointRequest(string Url);

public record EndpointResponse(Guid Id, string Url, bool IsActive, DateTime CreatedAt);

// The secret is only shown once, when the endpoint is created.
public record CreatedEndpointResponse(Guid Id, string Url, string Secret, DateTime CreatedAt);

public record PublishEventRequest(string Type, JsonElement? Payload);

public record PublishEventResponse(Guid EventId, int DeliveriesCreated);

public record DeliveryResponse(
    Guid Id,
    Guid EventId,
    string EventType,
    Guid EndpointId,
    string EndpointUrl,
    string Status,
    int AttemptCount,
    DateTime NextAttemptAt,
    int? LastStatusCode,
    string? LastError,
    DateTime CreatedAt,
    DateTime? DeliveredAt);
