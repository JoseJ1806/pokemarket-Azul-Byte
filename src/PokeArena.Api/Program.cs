using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog;
using PokeArena.Api.Modules.Identity;
using PokeArena.Api.Modules.Images;
using PokeArena.Api.Modules.Locations;
using PokeArena.Api.Modules.Catalog.Domain;

// ARCHIVO PRINCIPAL DE LA API.
// Aquí se registra la configuración, PostgreSQL y los módulos. La autenticación y la
// autorización viven desde el Sprint 2 en el módulo Identidad (IdentityModule).
// También se define GET /api/cards, que recibe filtros HTTP, consulta Card mediante
// PokeArenaDbContext y devuelve el JSON que consume el cliente de PokeArena.
// Crea el constructor de la aplicación y carga appsettings, variables de entorno
// y los servicios propios de ASP.NET Core.
var builder = WebApplication.CreateBuilder(args);

// Registra la documentación OpenAPI disponible durante el desarrollo.
builder.Services.AddOpenApi();

// MÓDULO IDENTIDAD Y ACCESO (Sprint 2).
// Antes aquí estaba la validación de JWT y la política AdminOnly. Se movieron a
// Modules/Identity/IdentityModule.cs, que ahora también registra el inicio de sesión
// con Google. Ahí se explica cada paso.
builder.Services.AddIdentityModule(builder.Configuration);

// Conecta la API con PostgreSQL y permite inyectar el DbContext en los endpoints.
builder.Services.AddDbContext<PokeArenaDbContext>(options =>
    // La cadena DefaultConnection se encuentra en la configuración de la API.
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));
// Cada módulo registra sus puertos, adaptadores y casos de uso.
builder.Services.AddImagesModule();
builder.Services.AddCatalogModule();
builder.Services.AddLocationsModule();

// Construye la aplicación con todos los servicios registrados arriba.
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // OpenAPI permite inspeccionar los endpoints mientras se desarrolla localmente.
    app.MapOpenApi();
}

app.UseHttpsRedirection();
// El orden importa: primero se identifica al usuario y luego se comprueba su rol.
// Por eso RequireAuthorization puede producir 401 o 403 correctamente.
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// Endpoint de búsqueda: recibe filtros HTTP y devuelve una página de cartas.
app.MapGet("/api/cards", async (
    // q es el nombre de carta; los demás parámetros son filtros independientes.
    string? q,
    string? code,
    int? cost,
    int? minCost,
    int? maxCost,
    int? power,
    int? minPower,
    int? maxPower,
    string? type,
    string? rarity,
    string? abilityCode,
    string? abilityTrigger,
    string? abilityEffect,
    int? page,
    int? pageSize,
    string? cursor,
    PokeArenaDbContext db,
    CancellationToken cancellationToken) =>
{
    // Los parámetros llegan desde la URL y PokeArenaDbContext llega por inyección
    // de dependencias, configurada al inicio de este mismo archivo.
    // Se usa cursor para recorrer resultados; page > 1 se rechaza para no mezclar
    // paginación por desplazamiento con el recorrido estable.
    // page solo conserva la primera página del contrato; para continuar se usa cursor.
    if (page is < 1)
        return Results.BadRequest(new { error = "page debe ser mayor o igual a 1." });

    if (page is > 1)
        return Results.BadRequest(new { error = "page > 1 no está disponible; use nextCursor." });

    // Limita el tamaño de cada respuesta para proteger tiempo y memoria.
    if (pageSize is < 1 or > 100)
        return Results.BadRequest(new { error = "pageSize debe estar entre 1 y 100." });

    if (!string.IsNullOrWhiteSpace(cursor) && page.HasValue)
        return Results.BadRequest(new { error = "page no se puede combinar con cursor." });

    // Aplica valores predeterminados cuando el cliente no envía paginación.
    var currentPage = page ?? 1;
    var currentPageSize = pageSize ?? 20;
    CardCursor? parsedCursor = null;

    // Si llega cursor, se convierte desde Base64 y JSON a CardCursor.
    if (!string.IsNullOrWhiteSpace(cursor))
    {
        try
        {
            // Primero se decodifica el texto URL-safe y luego se interpreta el JSON.
            var json = Encoding.UTF8.GetString(Base64UrlDecode(cursor));
            parsedCursor = JsonSerializer.Deserialize<CardCursor>(json);
            // Un cursor manipulado o inconsistente no puede cambiar el recorrido.
            if (parsedCursor is null || parsedCursor.LastId < 0 || parsedCursor.SnapshotId < parsedCursor.LastId)
                throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Results.BadRequest(new { error = "cursor no es válido." });
        }
    }

    // VALIDACIÓN DE FILTROS.
    // Se hace ANTES de tocar la base de datos: un filtro inválido se rechaza sin gastar
    // una consulta. Los rangos vienen de CardRules, la misma fuente que usa Crear carta.
    // El formato del error se mantiene igual que el resto de esta búsqueda.

    // Costo: exacto o límites del rango, todos entre MinCost y MaxCost.
    if (cost.HasValue && !CardRules.IsValidCost(cost.Value))
        return Results.BadRequest(new { error = $"cost debe estar entre {CardRules.MinCost} y {CardRules.MaxCost}." });
    if (minCost.HasValue && !CardRules.IsValidCost(minCost.Value))
        return Results.BadRequest(new { error = $"minCost debe estar entre {CardRules.MinCost} y {CardRules.MaxCost}." });
    if (maxCost.HasValue && !CardRules.IsValidCost(maxCost.Value))
        return Results.BadRequest(new { error = $"maxCost debe estar entre {CardRules.MinCost} y {CardRules.MaxCost}." });

    // Poder: mismo patrón, con el rango -20 a 20 (negativos incluidos).
    if (power.HasValue && !CardRules.IsValidPower(power.Value))
        return Results.BadRequest(new { error = $"power debe estar entre {CardRules.MinPower} y {CardRules.MaxPower}." });
    if (minPower.HasValue && !CardRules.IsValidPower(minPower.Value))
        return Results.BadRequest(new { error = $"minPower debe estar entre {CardRules.MinPower} y {CardRules.MaxPower}." });
    if (maxPower.HasValue && !CardRules.IsValidPower(maxPower.Value))
        return Results.BadRequest(new { error = $"maxPower debe estar entre {CardRules.MinPower} y {CardRules.MaxPower}." });

    // Un rango con el mínimo mayor que el máximo no puede dar resultados coherentes.
    if (minCost.HasValue && maxCost.HasValue && minCost > maxCost)
        return Results.BadRequest(new { error = "minCost no puede ser mayor que maxCost." });
    if (minPower.HasValue && maxPower.HasValue && minPower > maxPower)
        return Results.BadRequest(new { error = "minPower no puede ser mayor que maxPower." });

    // Tipo y rareza: si se envían, deben existir. Antes un tipo inexistente devolvía
    // una lista vacía, y la user story pide rechazarlo indicando el criterio.
    if (!string.IsNullOrWhiteSpace(type) && !IsKnownValue(type, CardRules.TypePrefixes.Keys))
        return Results.BadRequest(new
        {
            error = $"type no es válido. Valores permitidos: {string.Join(", ", CardRules.TypePrefixes.Keys)}."
        });
    if (!string.IsNullOrWhiteSpace(rarity) && !IsKnownValue(rarity, CardRules.Rarities))
        return Results.BadRequest(new
        {
            error = $"rarity no es válida. Valores permitidos: {string.Join(", ", CardRules.Rarities)}."
        });

    // El límite fija el conjunto inicial: cartas creadas después no alteran el recorrido.
    // En la primera petición se toma el mayor Id actual; las siguientes reutilizan
    // ese límite para que una carta nueva no aparezca a mitad del recorrido.
    var snapshotId = parsedCursor?.SnapshotId ?? await db.Cards
        .Select(card => (int?)card.Id)
        .MaxAsync(cancellationToken) ?? 0;


    // AsNoTracking indica que solo se leerán cartas; EF no necesita preparar cambios.
    // La consulta permanece en PostgreSQL; no se carga el catálogo completo en memoria.
    var query = db.Cards.AsNoTracking();
    // Solo se consideran cartas pertenecientes al snapshot inicial.
    query = query.Where(card => card.Id <= snapshotId);

    // Para la siguiente página se continúa después del último Id ya entregado.
    if (parsedCursor is not null)
        query = query.Where(card => card.Id > parsedCursor.LastId);

    // Cada bloque siguiente agrega una condición AND a la consulta actual.
    // Por eso una carta debe cumplir todos los criterios enviados.
    if (!string.IsNullOrWhiteSpace(q))
    {
        // EscapeLike evita que % y _ reciban significado especial en SQL LIKE.
        var searchTerm = EscapeLike(q.Trim());
        // Normalize elimina tildes del término recibido antes de compararlo.
        var normalizedSearchTerm = Normalize(searchTerm);

        // Unaccent hace la misma normalización en la columna Name de PostgreSQL.
        query = query.Where(card => EF.Functions.Unaccent(card.Name) != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.Name), $"%{normalizedSearchTerm}%"));
    }

    if (!string.IsNullOrWhiteSpace(code))
    {
        // El código usa coincidencia parcial y las mismas reglas de texto que Name.
        var searchCode = EscapeLike(code.Trim());
        var normalizedSearchCode = Normalize(searchCode);

        query = query.Where(card => EF.Functions.Unaccent(card.Code) != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.Code), $"%{normalizedSearchCode}%"));
    }

    if (cost.HasValue)
        // Con costo exacto solo se conservan cartas con igualdad numérica.
        query = query.Where(card => card.Cost == cost.Value);

    if (minCost.HasValue)
        // minCost incluye cartas desde ese valor.
        query = query.Where(card => card.Cost >= minCost.Value);

    if (maxCost.HasValue)
        // maxCost incluye cartas hasta ese valor.
        query = query.Where(card => card.Cost <= maxCost.Value);

    if (power.HasValue)
        // power exacto compara el entero almacenado.
        query = query.Where(card => card.Power == power.Value);

    if (minPower.HasValue)
        // minPower establece el inicio del rango de poder.
        query = query.Where(card => card.Power >= minPower.Value);

    if (maxPower.HasValue)
        // maxPower establece el final del rango de poder.
        query = query.Where(card => card.Power <= maxPower.Value);

    if (!string.IsNullOrWhiteSpace(type))
    {
        // Type se compara con el valor ingresado manualmente en la carta.
        var searchType = EscapeLike(type.Trim());
        var normalizedType = Normalize(searchType);

        query = query.Where(card => EF.Functions.Unaccent(card.Type) != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.Type), normalizedType));
    }

    if (!string.IsNullOrWhiteSpace(rarity))
    {
        // Rarity también proviene de la carga manual y no de un catálogo externo.
        var searchRarity = EscapeLike(rarity.Trim());
        var normalizedRarity = Normalize(searchRarity);

        query = query.Where(card => EF.Functions.Unaccent(card.Rarity) != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.Rarity), normalizedRarity));
    }

    if (!string.IsNullOrWhiteSpace(abilityCode))
    {
        // AbilityCode busca la habilidad por código o detecta el valor especial.
        var searchAbilityCode = EscapeLike(abilityCode.Trim());

        if (Normalize(searchAbilityCode).Equals("sin habilidad", StringComparison.OrdinalIgnoreCase))
            // Null o texto vacío representan una carta sin habilidad.
            query = query.Where(card => card.AbilityCode == null || card.AbilityCode == "");
        else
        {
            // Para códigos normales se mantiene la coincidencia parcial.
            var normalizedAbilityCode = Normalize(searchAbilityCode);
            query = query.Where(card => card.AbilityCode != null &&
                EF.Functions.ILike(EF.Functions.Unaccent(card.AbilityCode), $"%{normalizedAbilityCode}%"));
        }
    }

    if (!string.IsNullOrWhiteSpace(abilityTrigger))
    {
        // Filtra por el texto del evento que activa la habilidad.
        var searchTrigger = EscapeLike(abilityTrigger.Trim());
        var normalizedTrigger = Normalize(searchTrigger);

        query = query.Where(card =>
            card.AbilityTrigger != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.AbilityTrigger), $"%{normalizedTrigger}%"));
    }

    if (!string.IsNullOrWhiteSpace(abilityEffect))
    {
        // Filtra por el texto del efecto producido por la habilidad.
        var searchEffect = EscapeLike(abilityEffect.Trim());
        var normalizedEffect = Normalize(searchEffect);

        query = query.Where(card =>
            card.AbilityEffect != null &&
            EF.Functions.ILike(EF.Functions.Unaccent(card.AbilityEffect), $"%{normalizedEffect}%"));
    }

    // Estas operaciones se traducen a SQL y se ejecutan en PostgreSQL.
    var total = await query.CountAsync(cancellationToken);

    // Se solicita un registro adicional para saber si existe la siguiente página.
    // Ese registro extra no se devuelve al cliente.
    var cards = await query
        .OrderBy(card => card.Id)
        .Take(currentPageSize + 1)
        .Select(card => new
        {
            card.Id,
            card.Code,
            card.Name,
            card.ImageUrl,
            card.ThumbnailUrl,
            card.ImageStatus,
            card.IsAvailable,
            card.Cost,
            card.Power,
            card.Type,
            card.Rarity,
            card.AbilityCode,
            card.AbilityTrigger,
            card.AbilityEffect,
            card.AbilityAffectedType
        })
        .ToListAsync(cancellationToken);

    // Si llegaron más elementos que el límite, se marca que hay otra página.
    var hasNextPage = cards.Count > currentPageSize;
    if (hasNextPage)
        cards.RemoveAt(cards.Count - 1);

    // El cursor apunta al último elemento visible y conserva snapshotId.
    var nextCursor = hasNextPage
        ? EncodeCursor(new CardCursor(cards[^1].Id, snapshotId))
        : null;

    // Results.Ok serializa este objeto como JSON con paginación y cartas.
    return Results.Ok(new
    {
        page = currentPage,
        pageSize = currentPageSize,
        total,
        nextCursor,
        items = cards
    });
})
    .WithName("SearchCards")
    .RequireAuthorization("AdminOnly");

// Devuelve las habilidades existentes para que el formulario de Crear Carta
// seleccione un código válido, incluyendo HAB-000.
app.MapGet("/api/abilities", async (
    PokeArenaDbContext db,
    CancellationToken cancellationToken) =>
    Results.Ok(await db.Abilities.AsNoTracking()
        .OrderBy(ability => ability.Code)
        .Select(ability => new
        {
            ability.Code,
            ability.Name,
            ability.Trigger,
            ability.Effect,
            ability.AffectedType,
            ability.IsEvolution,
            ability.TargetCardCode
        })
        .ToListAsync(cancellationToken)))
    .WithName("ListAbilities")
    .RequireAuthorization("AdminOnly");

// Los endpoints de Crear Carta viven en el módulo Catálogo (Modules/Catalog/Http).
app.MapCatalogEndpoints();
app.MapLocationsEndpoints();
// POST /api/auth/google y GET /api/me (Sprint 2, Inicio de sesión con proveedor).
app.MapIdentityEndpoints();

app.Run();

static string EscapeLike(string value) =>
    value.Replace("\\", "\\\\")
         .Replace("%", "\\%")
         .Replace("_", "\\_");

// Elimina tildes antes de comparar texto con la función unaccent de PostgreSQL.
static string Normalize(string value) => value.Normalize(NormalizationForm.FormD)
    .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
    .Aggregate(new StringBuilder(), (builder, character) => builder.Append(character))
    .ToString()
    .Normalize(NormalizationForm.FormC);

// Indica si 'valor' coincide con alguno de los valores válidos, sin distinguir
// mayúsculas ni tildes. Usa Normalize para que "electrico" y "Eléctrico" cuenten
// como lo mismo, igual que ya hace el filtro de la consulta SQL.
static bool IsKnownValue(string valor, IEnumerable<string> valoresValidos)
{
    var normalizado = Normalize(valor.Trim());
    return valoresValidos.Any(valido =>
        Normalize(valido).Equals(normalizado, StringComparison.OrdinalIgnoreCase));
}

// Lee el cursor que la API devolvió en una respuesta anterior.
static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(
    value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

// Empaqueta la última carta y el límite del snapshot para la siguiente página.
static string EncodeCursor(CardCursor cursor) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)))
    .Replace('+', '-').Replace('/', '_').TrimEnd('=');

// LastId marca desde dónde continuar y SnapshotId fija el conjunto inicial.
record CardCursor(int LastId, int SnapshotId);

public partial class Program;