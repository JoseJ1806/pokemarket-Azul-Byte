using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Models;

namespace PokeArena.Api.Data;

// CAPA DE ACCESO A DATOS.
// Program.cs recibe esta clase por inyección de dependencias y la usa para construir
// consultas SQL. Entity Framework compara esta configuración con las migraciones.
public class PokeArenaDbContext(DbContextOptions<PokeArenaDbContext> options)
    : DbContext(options)
{
    // Representa la tabla cards y permite consultarla desde la API con EF Core.
    public DbSet<Card> Cards => Set<Card>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Este bloque define cómo Card se convierte en la tabla cards: nombres,
        // tamaños, obligatoriedad, unicidad e índices para acelerar la búsqueda.
        modelBuilder.Entity<Card>(entity =>
        {
            entity.ToTable("cards");

            entity.HasKey(card => card.Id);

            entity.Property(card => card.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(card => card.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(card => card.ImageUrl)
                .HasMaxLength(2048);

            entity.Property(card => card.ImageStatus)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(card => card.Type)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(card => card.Rarity)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(card => card.AbilityCode)
                .HasMaxLength(50);

            entity.Property(card => card.AbilityTrigger)
                .HasMaxLength(200);

            entity.Property(card => card.AbilityEffect)
                .HasMaxLength(1000);

            entity.HasIndex(card => card.Code).IsUnique();
            entity.HasIndex(card => card.Name);
            entity.HasIndex(card => new { card.Cost, card.Power });
        });
    }
}