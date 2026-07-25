using JnnBoost.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JnnBoost.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Licenca> Licencas => Set<Licenca>();
    public DbSet<TentativaBloqueada> TentativasBloqueadas => Set<TentativaBloqueada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Licenca>()
            .HasIndex(l => l.ChaveLicenca)
            .IsUnique();

        modelBuilder.Entity<TentativaBloqueada>()
            .HasOne(t => t.Licenca)
            .WithMany(l => l.TentativasBloqueadas)
            .HasForeignKey(t => t.LicencaId);
    }
}
