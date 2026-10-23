using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Services;

// ARCHIVO PRINCIPAL DE LA API.
// Aquí se registra la configuración, autenticación, autorización y PostgreSQL.
// También se define GET /api/cards, que recibe filtros HTTP, consulta Card mediante
// PokeArenaDbContext y devuelve el JSON que consume el cliente de PokeArena.
// Crea el constructor de la aplicación y carga appsettings, variables de entorno
// y los servicios propios de ASP.NET Core.
var builder = WebApplication.CreateBuilder(args);

// Registra la documentación OpenAPI disponible durante el desarrollo.
builder.Services.AddOpenApi();

// La clave se obtiene de configuración segura; no se guarda en el código fuente.
var signingKey = builder.Configuration["Jwt:SigningKey"];
// Si no existe la clave, la API no inicia porque no podría validar tokens confiables.
if (string.IsNullOrWhiteSpace(signingKey))
    throw new InvalidOperationException("Jwt:SigningKey debe configurarse mediante secretos o variables de entorno.");

builder.Services
    // Indica que las peticiones usan el esquema estándar Authorization: Bearer.
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Define qué debe comprobarse antes de considerar válido un JWT.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // El token debe estar firmado con la clave configurada.
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            // Si se configuran emisor y audiencia, también se validan.
            ValidateIssuer = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"]),
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]),
            ValidAudience = builder.Configuration["Jwt:Audience"],
            // Los tokens vencidos se rechazan y solo se tolera un minuto de diferencia.
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(options =>
    // Solo los usuarios con el rol emitido por el sistema de identidad pueden buscar.
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin")));

// Conecta la API con PostgreSQL y permite inyectar el DbContext en los endpoints.
builder.Services.AddDbContext<PokeArenaDbContext>(options =>
    // La cadena DefaultConnection se encuentra en la configuración de la API.
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddSingleton<CardImageService>();

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

    // El límite fija el conjunto inicial: cartas creadas después no alteran el recorrido.
    // En la primera petición se toma el mayor Id actual; las siguientes reutilizan
    // ese límite para que una carta nueva no aparezca a mitad del recorrido.
    var snapshotId = parsedCursor?.SnapshotId ?? await db.Cards
        .Select(card => (int?)card.Id)
        .MaxAsync(cancellationToken) ?? 0;

// Costo es un entero con rango de negocio de 0 a 10.
if (cost.HasValue && (cost < 0 || cost > 10))
{
    return Results.BadRequest(new
    {
        error = "cost debe estar entre 0 y 10."
    });
}

// El límite inferior usa las mismas reglas que el costo exacto.
if (minCost.HasValue && (minCost < 0 || minCost > 10))
{
    return Results.BadRequest(new
    {
        error = "minCost debe estar entre 0 y 10."
    });
}

// El límite superior también se valida antes de consultar la base.
if (maxCost.HasValue && (maxCost < 0 || maxCost > 10))
{
    return Results.BadRequest(new
    {
        error = "maxCost debe estar entre 0 y 10."
    });
}

// Poder debe ser no negativo y múltiplo de diez.
if (power.HasValue && (power < 0 || power % 10 != 0))
{
    return Results.BadRequest(new
    {
        error = "power debe ser un múltiplo de 10 no negativo."
    });
}

// Valida el límite mínimo de poder antes de construir el filtro SQL.
if (minPower.HasValue && (minPower < 0 || minPower % 10 != 0))
{
    return Results.BadRequest(new
    {
        error = "minPower debe ser un múltiplo de 10 no negativo."
    });
}

// Valida el límite máximo de poder antes de construir el filtro SQL.
if (maxPower.HasValue && (maxPower < 0 || maxPower % 10 != 0))
{
    return Results.BadRequest(new
    {
        error = "maxPower debe ser un múltiplo de 10 no negativo."
    });
}




    // Un intervalo invertido no puede devolver resultados coherentes.
    if (minCost.HasValue && maxCost.HasValue && minCost > maxCost)
    {
        return Results.BadRequest(new
        {
            error = "minCost no puede ser mayor que maxCost."
        });
    }

    // La misma regla se aplica al intervalo de poder.
    if (minPower.HasValue && maxPower.HasValue && minPower > maxPower)
    {
        return Results.BadRequest(new
        {
            error = "minPower no puede ser mayor que maxPower."
        });
    }

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

// Endpoint MVP para que un administrador cree una carta con una imagen.
// La petición multipart/form-data contiene los campos de la carta y el archivo image.
app.MapPost("/api/cards", async (
    [FromForm] CreateCardRequest request,
    HttpRequest httpRequest,
    PokeArenaDbContext db,
    CardImageService imageService,
    IWebHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(idempotencyKey))
        return Results.BadRequest(new { error = "Idempotency-Key es obligatorio." });

    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "name es obligatorio." });

    if (request.Cost is < 0 or > 10)
        return Results.BadRequest(new { error = "cost debe estar entre 0 y 10." });

    if (request.Power is < -20 or > 20)
        return Results.BadRequest(new { error = "power debe estar entre -20 y 20." });

    if (!CardRules.TypePrefixes.TryGetValue(request.Type ?? string.Empty, out var prefix))
        return Results.BadRequest(new { error = "type debe ser Fuego, Agua, Planta, Eléctrico, Psíquico o Lucha." });

    if (!CardRules.Rarities.Contains(request.Rarity ?? string.Empty))
        return Results.BadRequest(new { error = "rarity debe ser Común, Poco común, Rara o Legendaria." });

    if (request.Image is null)
        return Results.BadRequest(new { error = "image es obligatorio." });

    var abilityCode = request.AbilityCode?.Trim();
    if (string.IsNullOrWhiteSpace(abilityCode))
        return Results.BadRequest(new { error = "abilityCode es obligatorio; seleccione HAB-000 si no tiene habilidad." });

    var ability = await db.Abilities.SingleOrDefaultAsync(
        item => item.Code == abilityCode,
        cancellationToken);
    if (ability is null)
        return Results.BadRequest(new { error = "abilityCode no existe." });

    await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable, cancellationToken);
    await db.Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(hashtext({0}))", new object[] { idempotencyKey }, cancellationToken);

    var existing = await db.Cards.SingleOrDefaultAsync(
        card => card.IdempotencyKey == idempotencyKey,
        cancellationToken);
    if (existing is not null)
        return Results.Ok(existing);

    await db.Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(hashtext({0}))", new object[] { prefix }, cancellationToken);
    var code = await GenerateCardCodeAsync(db, prefix, cancellationToken);

    if (ability.IsEvolution)
    {
        if (string.IsNullOrWhiteSpace(ability.TargetCardCode))
            return Results.BadRequest(new { error = "La habilidad de evolución debe tener una carta destino." });

        if (await EvolutionCreatesCycleAsync(db, code, ability.TargetCardCode, cancellationToken))
            return Results.BadRequest(new { error = "La habilidad de evolución crea un ciclo o apunta a la misma carta." });
    }

    var rootPath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var originalPath = Path.Combine(rootPath, "uploads", "cards", code + "-large" + GetImageExtension(request.Image.ContentType));
    var thumbnailPath = Path.Combine(rootPath, "uploads", "cards", code + "-small.webp");

    var imageResult = await imageService.ValidateAndStoreAsync(
        request.Image, originalPath, thumbnailPath, cancellationToken);
    if (!imageResult.IsValid)
        return Results.BadRequest(new { error = imageResult.Error });

    var card = new Card
    {
        Code = code,
        Name = request.Name.Trim(),
        ImageUrl = $"/uploads/cards/{Path.GetFileName(originalPath)}",
        ThumbnailUrl = $"/uploads/cards/{Path.GetFileName(thumbnailPath)}",
        ImageStatus = "Lista",
        IsAvailable = true,
        IdempotencyKey = idempotencyKey,
        Cost = request.Cost,
        Power = request.Power,
        Type = request.Type!,
        Rarity = request.Rarity!,
        AbilityCode = ability.Code,
        AbilityTrigger = ability.Trigger,
        AbilityEffect = ability.Effect,
        AbilityAffectedType = ability.AffectedType
    };

    db.Cards.Add(card);
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Results.Created($"/api/cards/{card.Id}", card);
})
    .WithName("CreateCard")
    .DisableAntiforgery()
    .RequireAuthorization("AdminOnly");

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

// Lee el cursor que la API devolvió en una respuesta anterior.
static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(
    value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

// Empaqueta la última carta y el límite del snapshot para la siguiente página.
static string EncodeCursor(CardCursor cursor) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)))
    .Replace('+', '-').Replace('/', '_').TrimEnd('=');

static async Task<string> GenerateCardCodeAsync(
    PokeArenaDbContext db,
    string prefix,
    CancellationToken cancellationToken)
{
    var codes = await db.Cards
        .Where(card => card.Code.Contains("-" + prefix + "-"))
        .Select(card => card.Code)
        .ToListAsync(cancellationToken);
    var next = codes
        .Select(code => int.TryParse(code[(code.LastIndexOf('-') + 1)..], out var number) ? number : 0)
        .DefaultIfEmpty(0)
        .Max() + 1;
    return $"PKA-{prefix}-{next:000}";
}

static async Task<bool> EvolutionCreatesCycleAsync(
    PokeArenaDbContext db,
    string newCardCode,
    string targetCardCode,
    CancellationToken cancellationToken)
{
    var visited = new HashSet<string>(StringComparer.Ordinal);
    var currentCode = targetCardCode;

    while (!string.IsNullOrWhiteSpace(currentCode))
    {
        if (currentCode.Equals(newCardCode, StringComparison.Ordinal) || !visited.Add(currentCode))
            return true;

        var targetAbilityCode = await db.Cards
            .Where(card => card.Code == currentCode)
            .Select(card => card.AbilityCode)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(targetAbilityCode))
            return false;

        currentCode = await db.Abilities
            .Where(ability => ability.Code == targetAbilityCode)
            .Select(ability => ability.TargetCardCode)
            .SingleOrDefaultAsync(cancellationToken);
    }

    return false;
}

static string GetImageExtension(string? contentType) => contentType?.ToLowerInvariant() switch
{
    "image/png" => ".png",
    "image/jpeg" => ".jpg",
    "image/webp" => ".webp",
    _ => ".upload"
};

// LastId marca desde dónde continuar y SnapshotId fija el conjunto inicial.
record CardCursor(int LastId, int SnapshotId);

sealed class CreateCardRequest
{
    public string? Name { get; set; }

    public int Cost { get; set; }

    public int Power { get; set; }

    public string? Type { get; set; }

    public string? Rarity { get; set; }

    public string? AbilityCode { get; set; }

    public IFormFile? Image { get; set; }
}

static class CardRules
{
    public static readonly IReadOnlyDictionary<string, string> TypePrefixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Fuego"] = "FUE",
            ["Agua"] = "AGU",
            ["Planta"] = "PLA",
            ["Eléctrico"] = "ELE",
            ["Psíquico"] = "PSI",
            ["Lucha"] = "LUC"
        };

    public static readonly IReadOnlySet<string> Rarities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Común", "Poco común", "Rara", "Legendaria"
        };
}

public partial class Program;