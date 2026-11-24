using Microsoft.Extensions.Logging;
using PokeArena.Api.Modules.Collection.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Collection.Application;

// Lo que pide el cliente al crear un deck. Subject es el claim "sub" del JWT: de ahí sale
// el jugador. Ningún dato del cuerpo de la petición indica de quién es el deck.
public sealed record CreateDeckCommand(
    string? Subject,
    string? IdempotencyKey,
    string? Name,
    IReadOnlyList<int>? CardIds);

// Resultado de crear un deck: creado, repetición idempotente o error de negocio.
public sealed record CreateDeckResult(Deck? Deck, bool IsReplay, DomainError? Error)
{
    public static CreateDeckResult Created(Deck deck) => new(deck, false, null);

    public static CreateDeckResult Replayed(Deck deck) => new(deck, true, null);

    public static CreateDeckResult Failed(DomainError error) => new(null, false, error);
}

// CASO DE USO: CREAR DECK (módulo Colección, capa de aplicación).
//
// Orden del flujo (el mismo patrón que Crear Carta):
// 1. Valida el jugador, la llave de idempotencia, el nombre y las cartas pedidas
//    (cantidad, repetidas). No toca la base de datos.
// 2. Si la llave ya creó un deck de este jugador, devuelve ese mismo deck (idempotencia).
// 3. Valida que el jugador POSEA todas las cartas, con una sola consulta.
// 4. REGIÓN CRÍTICA: transacción corta con el candado del jugador. Verifica de nuevo la
//    llave (otra solicitud pudo terminar mientras esta esperaba), verifica que el nombre
//    no esté repetido e inserta el deck con sus 12 cartas.
//
// Por qué un candado por jugador: la verificación del nombre es "leer y luego escribir".
// Sin candado, dos solicitudes simultáneas podrían pasar ambas la verificación. Los
// índices únicos de la tabla decks son la última barrera si algo se saltara el candado.
public sealed class CreateDeckUseCase(
    IDeckRepository decks,
    TimeProvider clock,
    ILogger<CreateDeckUseCase> logger)
{
    public async Task<CreateDeckResult> ExecuteAsync(CreateDeckCommand command, CancellationToken cancellationToken)
    {
        // El token de administrador de desarrollo tiene un "sub" que no es un Guid: es un
        // token válido pero no corresponde a un jugador.
        if (!Guid.TryParse(command.Subject, out var playerId))
            return Failed(DomainError.NotFound(
                GetCollectionUseCase.NotAPlayerCode,
                "La sesión actual no corresponde a una cuenta de jugador."));

        // ---- PASO 1. Validaciones que no necesitan la base de datos ----
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Invalid("missing-idempotency-key", "Idempotency-Key es obligatorio.", "Idempotency-Key");

        if (command.IdempotencyKey.Length > DeckRules.MaxIdempotencyKeyLength)
            return Invalid(
                "invalid-field",
                $"Idempotency-Key no puede superar {DeckRules.MaxIdempotencyKeyLength} caracteres.",
                "Idempotency-Key");

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Invalid("required-field", "name es obligatorio.", "name");

        if (name.Length > DeckRules.MaxNameLength)
            return Invalid("invalid-field", $"name no puede superar {DeckRules.MaxNameLength} caracteres.", "name");

        if (command.CardIds is null)
            return Invalid("required-field", "cardIds es obligatorio.", "cardIds");

        var cardIds = command.CardIds;

        // Cantidad exacta. El mensaje dice cuántas cartas llegaron (criterio de la historia).
        if (cardIds.Count != DeckRules.Size)
            return Invalid(
                "invalid-deck-size",
                $"El deck debe tener exactamente {DeckRules.Size} cartas y se enviaron {cardIds.Count}.",
                "cardIds");

        // Cartas repetidas: se rechaza indicando CUÁLES (criterio de la historia).
        var repeated = cardIds
            .GroupBy(cardId => cardId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(cardId => cardId)
            .ToList();
        if (repeated.Count > 0)
            return Invalid(
                "duplicate-cards",
                $"El deck tiene cartas repetidas: {string.Join(", ", repeated)}.",
                "cardIds");

        // ---- PASO 2. Repetición idempotente (un reintento de red con la misma llave) ----
        var existing = await decks.FindByIdempotencyKeyAsync(playerId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, name, cardIds);

        // ---- PASO 3. ¿El jugador posee todas las cartas? Una sola consulta de 12 ids ----
        var owned = await decks.GetOwnedCardIdsAsync(playerId, cardIds, cancellationToken);
        var notOwned = cardIds
            .Except(owned)
            .OrderBy(cardId => cardId)
            .ToList();
        if (notOwned.Count > 0)
            return Invalid(
                "card-not-owned",
                $"No posees estas cartas: {string.Join(", ", notOwned)}.",
                "cardIds");

        // ---- PASO 4. REGIÓN CRÍTICA: candado del jugador, verificación e inserción ----
        Deck? created = null;
        DeckAlreadyExistsException? lostTheRace = null;

        // "await using": al salir del bloque se libera el alcance. Si no se confirmó, la
        // transacción se cancela y el candado se suelta, así cualquier "return" temprano
        // o error no deja nada a medias.
        await using (var scope = await decks.BeginCreationAsync(playerId, cancellationToken))
        {
            // Otra solicitud con la misma llave pudo terminar mientras esta esperaba su turno.
            var concurrent = await scope.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
            if (concurrent is not null)
                return ReplayOrConflict(concurrent, name, cardIds);

            if (await scope.NameExistsAsync(name, cancellationToken))
                return NameTaken(name);

            var deck = Deck.Create(playerId, name, command.IdempotencyKey, cardIds, clock.GetUtcNow());
            scope.Add(deck);

            try
            {
                await scope.CommitAsync(cancellationToken);
                created = deck;
            }
            catch (DeckAlreadyExistsException exception)
            {
                // La última barrera (índice único) rechazó el deck. No se puede seguir
                // consultando dentro de esta transacción: PostgreSQL la dejó abortada.
                lostTheRace = exception;
            }
        }

        if (created is not null)
        {
            logger.LogInformation("El jugador {PlayerId} creó el deck {DeckId}.", playerId, created.Id);
            return CreateDeckResult.Created(created);
        }

        // Aquí la transacción ya se cerró. Se lee con una consulta nueva para saber con
        // quién se chocó: si fue la misma llave, es una repetición; si no, fue el nombre.
        logger.LogInformation(
            "La creación de un deck del jugador {PlayerId} chocó con otra solicitud ({Constraint}).",
            playerId,
            lostTheRace!.ConstraintName);

        var winner = await decks.FindByIdempotencyKeyAsync(playerId, command.IdempotencyKey, cancellationToken);
        return winner is not null ? ReplayOrConflict(winner, name, cardIds) : NameTaken(name);
    }

    // Misma llave y mismos datos es un reintento válido. Misma llave con datos distintos es un
    // uso incorrecto de la llave y se rechaza con 409. Las cartas se comparan como conjunto:
    // el orden en que se enviaron no importa.
    private static CreateDeckResult ReplayOrConflict(Deck existing, string name, IReadOnlyList<int> cardIds)
    {
        var sameRequest =
            existing.Name == name
            && existing.Cards.Select(card => card.CardId).Order().SequenceEqual(cardIds.Order());

        return sameRequest
            ? CreateDeckResult.Replayed(existing)
            : Failed(new DomainError(
                DomainErrorKind.Conflict,
                "idempotency-key-conflict",
                "La Idempotency-Key ya se usó con datos distintos. Use una llave nueva para crear otro deck.",
                "Idempotency-Key"));
    }

    private static CreateDeckResult NameTaken(string name) =>
        Failed(new DomainError(
            DomainErrorKind.Conflict,
            "deck-name-taken",
            $"Ya tienes un deck llamado \"{name}\". Elige otro nombre.",
            "name"));

    private static CreateDeckResult Invalid(string code, string message, string field) =>
        Failed(DomainError.Validation(code, message, field));

    private static CreateDeckResult Failed(DomainError error) => CreateDeckResult.Failed(error);
}
