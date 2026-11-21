using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Domain;

namespace PokeArena.CargaDatos;

// HERRAMIENTA DE DATOS DE PRUEBA PARA LA PRUEBA DE CARGA (tarea 222).
//
// Qué hace: aplica las migraciones de la API y agrega cartas de prueba hasta llegar a la
// cantidad pedida (10.000 por defecto). La prueba de carga de tests/carga mide la búsqueda
// GET /api/cards sobre esas cartas.
//
// Cómo se usa (con PostgreSQL de compose.yaml levantado):
//   dotnet run --project tools/PokeArena.CargaDatos -- 10000
// La conexión se toma de la variable ConnectionStrings__DefaultConnection o, si no existe,
// de la base local de desarrollo.
//
// Decisiones:
// - Datos deterministas: la semilla del generador es fija, así dos corridas generan las
//   mismas cartas y los resultados de la medición se pueden comparar.
// - Idempotente: las cartas de prueba llevan Idempotency-Key "carga-N". Si ya existen,
//   solo agrega las que faltan.
// - Los nombres llevan tildes (Dragón, Ígneo...) para que la búsqueda ejercite unaccent.
// - Los códigos siguen el formato real PKA-TIPO-NNN y continúan después del código más
//   alto de cada tipo, para no chocar con cartas creadas a mano.
// - No se generan imágenes: las cartas apuntan a una imagen de ejemplo con estado Lista,
//   porque la prueba mide la búsqueda y no la carga de imágenes.

// La clase tiene nombre y namespace propios porque la API ya define una clase Program
// pública (la usan las pruebas) y dos clases Program en el mismo namespace chocarían.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        const int DefaultCount = 10_000;
        const int BatchSize = 1_000;
        const string KeyPrefix = "carga-";
        const string DefaultConnection =
            "Host=localhost;Port=5432;Database=pokearena;Username=pokearena;Password=local_dev_password";

        var targetCount = args.Length > 0 && int.TryParse(args[0], out var requested) && requested > 0
            ? requested
            : DefaultCount;

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DefaultConnection;

        var options = new DbContextOptionsBuilder<PokeArenaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new PokeArenaDbContext(options);

        Console.WriteLine("Aplicando migraciones...");
        await db.Database.MigrateAsync();

        var existingSeedCards = await db.Cards.CountAsync(
            card => card.IdempotencyKey != null && card.IdempotencyKey.StartsWith(KeyPrefix));

        if (existingSeedCards >= targetCount)
        {
            Console.WriteLine($"Ya existen {existingSeedCards} cartas de prueba. No se agrega nada.");
            return;
        }

        // Número más alto ya usado en cada tipo, para continuar la numeración PKA-TIPO-NNN.
        var nextNumber = new Dictionary<string, int>();
        foreach (var prefix in CardRules.TypePrefixes.Values)
        {
            var codePrefix = $"PKA-{prefix}-";
            var codes = await db.Cards
                .Where(card => card.Code.StartsWith(codePrefix))
                .Select(card => card.Code)
                .ToListAsync();

            var highest = codes
                .Select(code => int.TryParse(code[codePrefix.Length..], out var number) ? number : 0)
                .DefaultIfEmpty(0)
                .Max();

            nextNumber[prefix] = highest + 1;
        }

        string[] creatures =
        [
            "Dragón", "Tortuga", "Ratón", "Búho", "Zorro", "Lobo", "Colibrí", "Serpiente", "Halcón", "Escarabajo",
            "Medusa", "Jaguar", "Pingüino", "Tiburón", "Camaleón", "Murciélago", "Ciervo", "León", "Oso", "Águila"
        ];
        string[] adjectives =
        [
            "Ígneo", "Eléctrico", "Sombrío", "Veloz", "Gélido", "Místico", "Rocoso", "Acuático", "Feroz", "Antiguo",
            "Salvaje", "Brillante", "Silencioso", "Colosal", "Diminuto", "Psíquico", "Volcánico", "Lunar", "Solar", "Tóxico"
        ];

        var types = CardRules.TypePrefixes.Keys.ToArray();

        // Distribución de rarezas: la mayoría Comunes y pocas Legendarias, como en un catálogo real.
        static string PickRarity(Random random) => random.Next(100) switch
        {
            < 55 => "Común",
            < 80 => "Poco común",
            < 95 => "Rara",
            _ => "Legendaria"
        };

        // Semilla fija: cada índice genera siempre la misma carta.
        var random = new Random(222);
        for (var i = 0; i < existingSeedCards; i++)
            GenerateCard(i);

        var toCreate = targetCount - existingSeedCards;
        Console.WriteLine($"Agregando {toCreate} cartas de prueba ({existingSeedCards} ya existían)...");

        var created = 0;
        while (created < toCreate)
        {
            var batch = new List<Card>(BatchSize);
            for (var j = 0; j < BatchSize && created < toCreate; j++, created++)
            {
                var card = GenerateCard(existingSeedCards + created);
                var prefix = CardRules.TypePrefixes[card.Type];
                card.Code = $"PKA-{prefix}-{nextNumber[prefix]++:000}";
                batch.Add(card);
            }

            db.Cards.AddRange(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Console.WriteLine($"  {existingSeedCards + created} / {targetCount}");
        }

        var total = await db.Cards.CountAsync();
        Console.WriteLine($"Listo. Cartas en la base: {total}.");

        foreach (var group in await db.Cards
                     .GroupBy(card => card.Rarity)
                     .Select(g => new { Rarity = g.Key, Count = g.Count() })
                     .OrderBy(g => g.Rarity)
                     .ToListAsync())
        {
            Console.WriteLine($"  {group.Rarity}: {group.Count}");
        }

        // Genera la carta número index. Avanza el generador siempre la misma cantidad de veces,
        // así una corrida que retoma desde la carta 5.000 produce las mismas que una completa.
        Card GenerateCard(int index)
        {
            var creature = creatures[random.Next(creatures.Length)];
            var adjective = adjectives[random.Next(adjectives.Length)];
            var type = types[random.Next(types.Length)];
            var rarity = PickRarity(random);
            var cost = random.Next(CardRules.MinCost, CardRules.MaxCost + 1);
            var power = random.Next(CardRules.MinPower, CardRules.MaxPower + 1);

            return new Card
            {
                Name = $"{creature} {adjective} {index + 1:00000}",
                ImageUrl = "/uploads/cards/carga-ejemplo.png",
                ThumbnailUrl = "/uploads/cards/carga-ejemplo-small.webp",
                ImageStatus = CardImageStatus.Ready,
                IsAvailable = true,
                IdempotencyKey = $"{KeyPrefix}{index + 1}",
                Cost = cost,
                Power = power,
                Type = type,
                Rarity = rarity,
                AbilityCode = CardRules.NoAbilityCode
            };
        }
    }
}
