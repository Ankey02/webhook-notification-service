namespace WebhookService.Api.Models;

public enum DeliveryStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

// A URL that wants to be notified (the merchant's server).
public class WebhookEndpoint
{
    public Guid Id { get; set; }
    public string Url { get; set; } = "";
    public string Secret { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

// Something that happened, e.g. "payment.captured".
public class WebhookEvent
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

// One message to send: one event to one endpoint, with retry tracking.
public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public WebhookEvent Event { get; set; } = null!;
    public Guid EndpointId { get; set; }
    public WebhookEndpoint Endpoint { get; set; } = null!;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
