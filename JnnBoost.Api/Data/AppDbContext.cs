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

        // Default de verdade no banco (não só no C#) - assim funciona mesmo
        // em INSERTs feitos diretamente via SQL, sem passar pela API.
        modelBuilder.Entity<Licenca>()
            .Property(l => l.CriadaEm)
            .HasDefaultValueSql("NOW()");

        modelBuilder.Entity<TentativaBloqueada>()
            .Property(t => t.TentativaEm)
            .HasDefaultValueSql("NOW()");

        modelBuilder.Entity<TentativaBloqueada>()
            .HasOne(t => t.Licenca)
            .WithMany(l => l.TentativasBloqueadas)
            .HasForeignKey(t => t.LicencaId);
    }
}
