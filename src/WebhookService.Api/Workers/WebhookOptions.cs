namespace WebhookService.Api.Workers;

public class WebhookOptions
{
    // Seconds to wait after each failed attempt. Max attempts = number of delays + 1.
    public int[] RetryDelaysSeconds { get; set; } = [];
    public int PollIntervalSeconds { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
}
