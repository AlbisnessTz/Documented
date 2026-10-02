using Documented.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Documented.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentItem> DocumentItems => Set<DocumentItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<BusinessProfile>().HasIndex(x => x.TenantId).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<Document>().HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        modelBuilder.Entity<Document>().HasIndex(x => x.PublicToken).IsUnique();

        modelBuilder.Entity<Document>().Property(x => x.Discount).HasPrecision(18, 2);
        modelBuilder.Entity<Document>().Property(x => x.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<Document>().Property(x => x.Total).HasPrecision(18, 2);
        modelBuilder.Entity<DocumentItem>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<DocumentItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<DocumentItem>().Property(x => x.LineTotal).HasPrecision(18, 2);

        modelBuilder.Entity<Tenant>()
            .HasOne(x => x.BusinessProfile)
            .WithOne(x => x.Tenant)
            .HasForeignKey<BusinessProfile>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tenant>()
            .HasMany(x => x.Users)
            .WithOne(x => x.Tenant)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasOne(x => x.Tenant)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasMany(x => x.Items)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}