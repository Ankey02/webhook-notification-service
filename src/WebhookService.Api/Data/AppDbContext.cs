using Microsoft.EntityFrameworkCore;
using WebhookService.Api.Models;

namespace WebhookService.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookEvent> Events => Set<WebhookEvent>();
    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WebhookEndpoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            e.Property(x => x.Secret).HasMaxLength(200).IsRequired();
        });

        b.Entity<WebhookEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        b.Entity<WebhookDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId);
            e.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId);
            // The background worker looks up "pending and due" rows all the time.
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
    }
}
