using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Domain;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Application;

public sealed record CreateCardCommand(
    string IdempotencyKey,
    string? Name,
    int Cost,
    int Power,
    string? Type,
    string? Rarity,
    string? AbilityCode,
    ImageUpload? Image);

// Resultado de crear una carta: creada, repetición idempotente o error de negocio.
public sealed record CreateCardResult(Card? Card, bool IsReplay, DomainError? Error)
{
    public static CreateCardResult Created(Card card) => new(card, false, null);

    public static CreateCardResult Replayed(Card card) => new(card, true, null);

    public static CreateCardResult Failed(DomainError error) => new(null, false, error);
}

// CASO DE USO: CREAR CARTA (módulo Catálogo de Cartas).
// Orden del flujo:
// 1. Valida los campos, la habilidad y la llave de idempotencia.
// 2. Si la llave ya creó una carta, devuelve esa misma carta.
// 3. Valida la imagen y prepara la versión pequeña, fuera de cualquier bloqueo.
// 4. Dentro de una unidad de trabajo exclusiva reserva el código, guarda las imágenes
//    y la carta. Si algo falla, borra los archivos para no dejar huérfanos.
public sealed class CreateCardUseCase(ICardRepository cards, ProcessImageUseCase images)
{
    private const string ImagesFolder = "uploads/cards";

    public async Task<CreateCardResult> ExecuteAsync(CreateCardCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Invalid("missing-idempotency-key", "Idempotency-Key es obligatorio.", "Idempotency-Key");

        if (command.IdempotencyKey.Length > CardRules.MaxIdempotencyKeyLength)
            return Invalid("invalid-field", $"Idempotency-Key no puede superar {CardRules.MaxIdempotencyKeyLength} caracteres.", "Idempotency-Key");

        if (string.IsNullOrWhiteSpace(command.Name))
            return Invalid("required-field", "name es obligatorio.", "name");

        if (command.Name.Trim().Length > CardRules.MaxNameLength)
            return Invalid("invalid-field", $"name no puede superar {CardRules.MaxNameLength} caracteres.", "name");

        if (command.Cost is < CardRules.MinCost or > CardRules.MaxCost)
            return Invalid("invalid-field", "cost debe estar entre 0 y 10.", "cost");

        if (command.Power is < CardRules.MinPower or > CardRules.MaxPower)
            return Invalid("invalid-field", "power debe estar entre -20 y 20.", "power");

        if (!CardRules.TypePrefixes.TryGetValue(command.Type ?? string.Empty, out var typePrefix))
            return Invalid("invalid-field", "type debe ser Fuego, Agua, Planta, Eléctrico, Psíquico o Lucha.", "type");

        if (!CardRules.Rarities.Contains(command.Rarity ?? string.Empty))
            return Invalid("invalid-field", "rarity debe ser Común, Poco común, Rara o Legendaria.", "rarity");

        if (command.Image is null)
            return Invalid("required-field", "image es obligatorio.", "image");

        var abilityCode = command.AbilityCode?.Trim();
        if (string.IsNullOrWhiteSpace(abilityCode))
            return Invalid("required-field", "abilityCode es obligatorio, seleccione HAB-000 si no tiene habilidad.", "abilityCode");

        var ability = await cards.FindAbilityAsync(abilityCode, cancellationToken);
        if (ability is null)
            return Invalid("invalid-field", "abilityCode no existe.", "abilityCode");

        // Un reintento de red con la misma llave devuelve la carta ya creada sin procesar la imagen.
        var existing = await cards.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, command, ability.Code);

        var prepared = images.Prepare(command.Image, ImageRequirements.ForCard, cancellationToken);
        if (prepared.Error is not null)
            return CreateCardResult.Failed(prepared.Error);

        await using var scope = await cards.BeginCreationAsync(command.IdempotencyKey, typePrefix, cancellationToken);

        // Otra solicitud con la misma llave pudo terminar mientras esta esperaba su turno.
        var concurrent = await scope.FindExistingAsync(cancellationToken);
        if (concurrent is not null)
            return ReplayOrConflict(concurrent, command, ability.Code);

        var code = await scope.ReserveNextCodeAsync(cancellationToken);

        if (ability.IsEvolution)
        {
            if (string.IsNullOrWhiteSpace(ability.TargetCardCode))
                return Invalid("invalid-evolution", "La habilidad de evolución debe tener una carta destino.", "abilityCode");

            if (await scope.EvolutionCreatesCycleAsync(code, ability.TargetCardCode, cancellationToken))
                return Invalid("evolution-cycle", "La habilidad de evolución crea un ciclo o apunta a la misma carta.", "abilityCode");
        }

        var stored = await images.StoreAsync(ImagesFolder, code, prepared.Image!, cancellationToken);
        try
        {
            // La carta solo queda disponible cuando existen ambas versiones de la imagen.
            var imageReady = stored.ThumbnailUrl is not null;
            var card = new Card
            {
                Code = code,
                Name = command.Name.Trim(),
                ImageUrl = stored.OriginalUrl,
                ThumbnailUrl = stored.ThumbnailUrl,
                ImageStatus = imageReady ? CardImageStatus.Ready : CardImageStatus.Failed,
                IsAvailable = imageReady,
                IdempotencyKey = command.IdempotencyKey,
                Cost = command.Cost,
                Power = command.Power,
                Type = command.Type!,
                Rarity = command.Rarity!,
                AbilityCode = ability.Code,
                AbilityTrigger = ability.Trigger,
                AbilityEffect = ability.Effect,
                AbilityAffectedType = ability.AffectedType
            };

            await scope.CommitAsync(card, cancellationToken);
            return CreateCardResult.Created(card);
        }
        catch
        {
            await images.DiscardAsync(stored, CancellationToken.None);
            throw;
        }
    }

    // Misma llave y mismos datos es un reintento válido. Misma llave con datos distintos es un
    // uso incorrecto de la llave y se rechaza con 409. La imagen no se compara porque no se
    // guarda su huella, solo los campos de la carta.
    private static CreateCardResult ReplayOrConflict(Card existing, CreateCardCommand command, string abilityCode)
    {
        var sameRequest =
            existing.Name == command.Name!.Trim()
            && existing.Cost == command.Cost
            && existing.Power == command.Power
            && existing.Type == command.Type
            && existing.Rarity == command.Rarity
            && existing.AbilityCode == abilityCode;

        return sameRequest
            ? CreateCardResult.Replayed(existing)
            : CreateCardResult.Failed(new DomainError(
                DomainErrorKind.Conflict,
                "idempotency-key-conflict",
                "La Idempotency-Key ya se usó con datos distintos. Use una llave nueva para crear otra carta.",
                "Idempotency-Key"));
    }

    private static CreateCardResult Invalid(string code, string message, string field) =>
        CreateCardResult.Failed(DomainError.Validation(code, message, field));
}
