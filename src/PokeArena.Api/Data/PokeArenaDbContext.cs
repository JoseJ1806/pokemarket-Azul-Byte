using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Locations.Domain; // NUEVO

namespace PokeArena.Api.Data;

// CAPA DE ACCESO A DATOS.
// Program.cs recibe esta clase por inyección de dependencias y la usa para construir
// consultas SQL. Entity Framework compara esta configuración con las migraciones.
public class PokeArenaDbContext(DbContextOptions<PokeArenaDbContext> options)
    : DbContext(options)
{
    // Representa la tabla cards y permite consultarla desde la API con EF Core.
    public DbSet<Card> Cards => Set<Card>();

    public DbSet<Ability> Abilities => Set<Ability>();

    public DbSet<Location> Locations => Set<Location>(); // NUEVO

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

            entity.Property(card => card.ThumbnailUrl)
                .HasMaxLength(2048);

            entity.Property(card => card.IdempotencyKey)
                .HasMaxLength(200);

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

            entity.Property(card => card.AbilityAffectedType)
                .HasMaxLength(50);

            entity.HasIndex(card => card.Code).IsUnique();
            entity.HasIndex(card => card.IdempotencyKey).IsUnique();
            entity.HasIndex(card => card.Name);
            entity.HasIndex(card => new { card.Cost, card.Power });
        });

        modelBuilder.Entity<Ability>(entity =>
        {
            entity.ToTable("abilities");
            entity.HasKey(ability => ability.Code);
            entity.Property(ability => ability.Code).HasMaxLength(50);
            entity.Property(ability => ability.Name).HasMaxLength(100).IsRequired();
            entity.Property(ability => ability.Trigger).HasMaxLength(200);
            entity.Property(ability => ability.Effect).HasMaxLength(1000);
            entity.Property(ability => ability.AffectedType).HasMaxLength(50);
            entity.Property(ability => ability.TargetCardCode).HasMaxLength(50);
        });

        modelBuilder.Entity<Ability>().HasData(new Ability
        {
            Code = "HAB-000",
            Name = "Sin Habilidad",
            IsEvolution = false
        });

        // NUEVO: tabla locations (historia Crear ubicación).
        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("locations");
            entity.HasKey(location => location.Id);

            entity.Property(location => location.Name)
                .HasMaxLength(LocationRules.MaxNameLength)
                .IsRequired();

            entity.Property(location => location.Description)
                .HasMaxLength(LocationRules.MaxDescriptionLength)
                .IsRequired();

            entity.Property(location => location.ImageUrl).HasMaxLength(2048);
            entity.Property(location => location.ThumbnailUrl).HasMaxLength(2048);

            entity.Property(location => location.ImageStatus)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(location => location.IdempotencyKey)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(location => location.EnergyMode)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(location => location.EffectType).HasMaxLength(50);
            entity.Property(location => location.EffectParameters).HasColumnType("jsonb");

            entity.HasIndex(location => location.IdempotencyKey).IsUnique();
            entity.HasIndex(location => location.Name);
        });
    }
}