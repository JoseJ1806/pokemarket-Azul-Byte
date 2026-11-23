using Microsoft.Extensions.Logging;
using PokeArena.Api.Modules.Collection.Domain;
using PokeArena.Api.Modules.Identity.Application;

namespace PokeArena.Api.Modules.Collection.Application;

// CASO DE USO: ENTREGAR LA COLECCIÓN INICIAL (módulo Colección, capa de aplicación).
//
// Es el "GrantInitialCollectionUseCase" del diagrama SD-04. Implementa el puerto
// IInitialCollectionGranter que definió el módulo Identidad: el login lo llama en CADA
// ingreso (decisión A), así que esta operación debe ser IDEMPOTENTE: si el jugador ya
// recibió su colección, no hace nada y responde AlreadyGranted.
//
// Qué entrega: 15 cartas Comunes DISTINTAS elegidas al azar y 1.000 cubos, todo en UNA sola
// transacción (todo o nada) y EXACTAMENTE UNA VEZ aunque lleguen dos primeros ingresos
// al mismo tiempo (candado por jugador más registro de entrega con clave primaria).
//
// Resultados posibles (InitialCollectionOutcome):
// - Granted:        se entregó AHORA.
// - AlreadyGranted: ya la había recibido; no se escribió nada.
// - NotAvailable:   hay menos de 15 Comunes disponibles; no se escribió nada. El jugador
//                   recibirá la colección en un ingreso posterior, cuando haya suficientes.
public sealed class GrantInitialCollectionUseCase(
    ICollectionRepository repository,
    ICommonCardSource commonCards,
    InitialCardSelector selector,
    TimeProvider clock,
    ILogger<GrantInitialCollectionUseCase> logger) : IInitialCollectionGranter
{
    public async Task<InitialCollectionOutcome> GrantIfMissingAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        // PASO 1. Abrir la transacción y tomar el candado de ESTE jugador. A partir de aquí,
        // si llega otra solicitud del mismo jugador, espera a que esta termine.
        // "await using" libera el alcance al salir del método: si no se confirmó, se cancela
        // todo, así que cualquier "return" o error temprano no deja nada a medias.
        await using var scope = await repository.BeginDeliveryAsync(playerId, cancellationToken);

        // PASO 2. ¿Ya recibió su entrega? Si sí, no hay nada más que hacer (idempotencia).
        if (await scope.HasInitialDeliveryAsync(cancellationToken))
            return InitialCollectionOutcome.AlreadyGranted;

        // PASO 3. Pedir al catálogo las Comunes disponibles. Distinct() por si llegaran
        // repetidas: lo que cuenta son cartas DISTINTAS.
        var available = await commonCards.GetAvailableCommonCardIdsAsync(cancellationToken);
        if (available.Distinct().Count() < InitialCollectionRules.InitialCardCount)
            return InitialCollectionOutcome.NotAvailable;

        // PASO 4. Elegir 15 distintas al azar. El azar viene del puerto IRandomSource.
        var cardIds = selector.Select(available);

        // PASO 5. Anotar lo que se va a guardar: el registro de entrega, las 15 copias y
        // el primer movimiento de cubos. Todavía no se escribe nada.
        var now = clock.GetUtcNow();
        scope.AddInitialDelivery(InitialDelivery.Create(playerId, now));
        scope.AddCopies(cardIds.Select(cardId => CardCopy.CreateInitial(playerId, cardId, now)).ToList());
        scope.AddCubeMovement(CubeMovement.CreateInitialGrant(playerId, now));

        // PASO 6. Guardar todo junto y confirmar. Si la base rechaza el registro porque otra
        // solicitud ya entregó (la última barrera), se interpreta como "ya estaba entregada".
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (InitialDeliveryAlreadyExistsException)
        {
            logger.LogInformation(
                "La entrega inicial del jugador {PlayerId} la completó otra solicitud al mismo tiempo.",
                playerId);
            return InitialCollectionOutcome.AlreadyGranted;
        }

        logger.LogInformation("Se entregó la colección inicial al jugador {PlayerId}.", playerId);
        return InitialCollectionOutcome.Granted;
    }
}
