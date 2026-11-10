using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Collection.Domain;
using PokeArena.Api.Modules.Identity.Domain;
using PokeArena.Api.Modules.Locations.Domain;

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

    // Sprint 2: cuentas de jugador creadas al iniciar sesión con Google.
    // Representa la tabla player_accounts (configurada al final de OnModelCreating).
    public DbSet<PlayerAccount> PlayerAccounts => Set<PlayerAccount>();

    // Sprint 2: colección inicial del jugador (módulo Colección). Cada DbSet representa una
    // tabla configurada al final de OnModelCreating.
    public DbSet<CardCopy> CardCopies => Set<CardCopy>();

    public DbSet<InitialDelivery> InitialDeliveries => Set<InitialDelivery>();

    public DbSet<CubeMovement> CubeMovements => Set<CubeMovement>();

    public DbSet<CubeBalance> CubeBalances => Set<CubeBalance>();

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

        // SPRINT 2: tabla player_accounts (US Inicio de sesión con proveedor).
        // Cada fila es una cuenta de jugador. Las reglas de creación están en
        // PlayerAccount.CreatePlayer y el acceso en EfPlayerAccountRepository.
        modelBuilder.Entity<PlayerAccount>(entity =>
        {
            entity.ToTable("player_accounts");
            entity.HasKey(account => account.Id);

            // El Guid lo genera la aplicación (Guid versión 7), no la base de datos.
            entity.Property(account => account.Id).ValueGeneratedNever();

            entity.Property(account => account.Provider)
                .HasMaxLength(50)
                .IsRequired();

            // Google documenta que "sub" tiene como máximo 255 caracteres.
            entity.Property(account => account.ProviderSubject)
                .HasMaxLength(255)
                .IsRequired();

            // 320 es el largo máximo de un correo según el estándar.
            entity.Property(account => account.Email)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(account => account.Role)
                .HasMaxLength(20)
                .IsRequired();

            // ÍNDICE ÚNICO: una persona de un proveedor tiene como máximo UNA cuenta.
            // Es lo que hace imposible duplicar cuentas con ingresos simultáneos, aunque
            // lleguen a instancias distintas de la API. Fíjese que NO incluye el correo:
            // la identidad es el proveedor más su identificador permanente.
            entity.HasIndex(account => new { account.Provider, account.ProviderSubject })
                .IsUnique();
        });

        // ---------------------------------------------------------------------------
        // MÓDULO COLECCIÓN (Sprint 2, US Recibir colección inicial).
        //
        // Decisiones de diseño (ver ADR-004):
        // - Una fila por copia de carta (decisión D).
        // - SIN llaves foráneas hacia player_accounts ni cards: los módulos Identidad y
        //   Catálogo no quedan atados a Colección a nivel de base de datos (decisiones G y 5).
        // - Origen y motivo se guardan como TEXTO, no como número.
        // - Los movimientos de cubos son la verdad del saldo, y el saldo se guarda aparte,
        //   en la misma transacción (decisión E).
        // ---------------------------------------------------------------------------

        // initial_deliveries: una fila por jugador que ya recibió su entrega inicial. Su clave
        // primaria es la BARRERA de "exactamente una vez": la base de datos es incapaz de
        // guardar dos entregas del mismo jugador (decisión B).
        modelBuilder.Entity<InitialDelivery>(entity =>
        {
            entity.ToTable("initial_deliveries");
            entity.HasKey(delivery => delivery.PlayerId);

            // El Guid lo asigna la aplicación (es el Id de la cuenta), no la base de datos.
            entity.Property(delivery => delivery.PlayerId).ValueGeneratedNever();
        });

        // card_copies: una fila por cada copia de carta de un jugador.
        modelBuilder.Entity<CardCopy>(entity =>
        {
            entity.ToTable("card_copies");
            entity.HasKey(copy => copy.Id);

            // Se guarda el nombre del valor ("Initial"), no su número.
            entity.Property(copy => copy.Origin)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            // ÍNDICE COMPUESTO (no único: un jugador puede tener varias copias de una carta).
            // Acelera la consulta más frecuente: "las cartas de este jugador, agrupadas por
            // carta con su cantidad de copias".
            entity.HasIndex(copy => new { copy.PlayerId, copy.CardId });
        });

        // cube_movements: el historial de cubos. SOLO se agregan filas, nunca se editan.
        modelBuilder.Entity<CubeMovement>(entity =>
        {
            // RESTRICCIÓN: un movimiento de cero cubos no tiene sentido.
            entity.ToTable("cube_movements", table =>
                table.HasCheckConstraint("CK_cube_movements_Amount_NotZero", "\"Amount\" <> 0"));
            entity.HasKey(movement => movement.Id);

            entity.Property(movement => movement.Reason)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            // Acelera "el historial de este jugador".
            entity.HasIndex(movement => new { movement.PlayerId, movement.Id });
        });

        // cube_balances: el saldo actual, una fila por jugador.
        modelBuilder.Entity<CubeBalance>(entity =>
        {
            // RESTRICCIÓN: el saldo nunca puede ser negativo (RNF-INT-03). La base de datos lo
            // garantiza aunque el código de la aplicación se equivocara.
            entity.ToTable("cube_balances", table =>
                table.HasCheckConstraint("CK_cube_balances_Balance_NotNegative", "\"Balance\" >= 0"));
            entity.HasKey(balance => balance.PlayerId);

            // El Guid lo asigna la aplicación (es el Id de la cuenta).
            entity.Property(balance => balance.PlayerId).ValueGeneratedNever();
        });
    }
}
