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
// Las reglas de entrada viven en CreateCardValidator; aquí solo queda el flujo:
// 1. Valida el comando y busca la habilidad.
// 2. Si la llave ya creó una carta, devuelve esa misma carta.
// 3. Valida la imagen y prepara la versión pequeña, fuera de cualquier bloqueo.
// 4. Dentro de una unidad de trabajo exclusiva reserva el código, guarda las imágenes
//    y la carta. Si algo falla, borra los archivos para no dejar huérfanos.
public sealed class CreateCardUseCase(ICardRepository cards, ProcessImageUseCase images)
{
    private const string ImagesFolder = "uploads/cards";

    public async Task<CreateCardResult> ExecuteAsync(CreateCardCommand command, CancellationToken cancellationToken)
    {
        var validation = CreateCardValidator.Validate(command);
        if (validation.Error is { } validationError)
            return CreateCardResult.Failed(validationError);

        var request = validation.Value!;

        var ability = await cards.FindAbilityAsync(request.AbilityCode, cancellationToken);
        if (ability is null)
            return Invalid("invalid-field", "abilityCode no existe.", "abilityCode");

        // Un reintento de red con la misma llave devuelve la carta ya creada sin procesar la imagen.
        var existing = await cards.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, request, ability.Code);

        var prepared = images.Prepare(request.Image, ImageRequirements.ForCard, cancellationToken);
        if (prepared.Error is not null)
            return CreateCardResult.Failed(prepared.Error);

        await using var scope = await cards.BeginCreationAsync(request.IdempotencyKey, request.TypePrefix, cancellationToken);

        // Otra solicitud con la misma llave pudo terminar mientras esta esperaba su turno.
        var concurrent = await scope.FindExistingAsync(cancellationToken);
        if (concurrent is not null)
            return ReplayOrConflict(concurrent, request, ability.Code);

        var code = await scope.ReserveNextCodeAsync(cancellationToken);

        if (ability.IsEvolution)
        {
            if (CreateCardValidator.ValidateAbility(ability) is { } abilityError)
                return CreateCardResult.Failed(abilityError);

            if (await scope.EvolutionCreatesCycleAsync(code, ability.TargetCardCode!, cancellationToken))
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
                Name = request.Name,
                ImageUrl = stored.OriginalUrl,
                ThumbnailUrl = stored.ThumbnailUrl,
                ImageStatus = imageReady ? CardImageStatus.Ready : CardImageStatus.Failed,
                IsAvailable = imageReady,
                IdempotencyKey = request.IdempotencyKey,
                Cost = request.Cost,
                Power = request.Power,
                Type = request.Type,
                Rarity = request.Rarity,
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
    private static CreateCardResult ReplayOrConflict(Card existing, ValidCardRequest request, string abilityCode)
    {
        var sameRequest =
            existing.Name == request.Name
            && existing.Cost == request.Cost
            && existing.Power == request.Power
            && existing.Type == request.Type
            && existing.Rarity == request.Rarity
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
