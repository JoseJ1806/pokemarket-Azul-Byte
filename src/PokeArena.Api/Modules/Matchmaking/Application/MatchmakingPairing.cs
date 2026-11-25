using Microsoft.Extensions.Logging;
using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Api.Modules.Matchmaking.Application;

// EMPAREJAMIENTO (módulo Emparejamiento, capa de aplicación).
//
// No hay un proceso aparte que recorra la fila: cada vez que un jugador busca o consulta su
// búsqueda, esta clase intenta emparejarlo. Como todas las instancias de la API leen la misma
// tabla y los bloqueos viven en PostgreSQL, funciona igual con una o con varias instancias.
//
// Tres pasos (ADR-007):
// 1. EMPAREJAR (transacción corta): se elige un rival, se bloquean las DOS filas en orden de Id
//    y se vuelve a comprobar que las dos siguen en espera. Solo entonces las dos pasan a
//    Pairing con un PairingId compartido. Una fila bloqueada solo la puede cambiar una
//    transacción a la vez, así que un jugador nunca queda en dos emparejamientos.
// 2. CREAR LA PARTIDA (fuera de la transacción): se pide al módulo Partida con la llave de
//    idempotencia del emparejamiento.
// 3. CERRAR: si la partida se creó, las dos búsquedas pasan a MatchFound con su id; si no, las
//    dos vuelven a Waiting con el motivo. Nunca queda un jugador con partida y el otro sin ella.
//
// Si la instancia se cae entre el paso 1 y el 3, las búsquedas quedan en Pairing. La siguiente
// consulta de cualquiera de los dos, pasados 10 segundos, repite los pasos 2 y 3 con la misma
// llave: o recupera la partida ya creada o la crea por primera vez.
public sealed class MatchmakingPairing(
    IMatchSearchRepository searches,
    IPairedMatchStarter matchStarter,
    TimeProvider clock,
    ILogger<MatchmakingPairing> logger)
{
    // markSeen indica que el jugador está consultando: renueva su última consulta.
    public async Task<MatchSearch?> ProcessAsync(Guid searchId, bool markSeen, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        PairedMatchRequest? request = null;
        MatchSearch? search;

        // PASO 1. EMPAREJAR (o preparar el reintento de un emparejamiento a medias).
        await using (var scope = await searches.BeginPairingScopeAsync(searchId, cancellationToken))
        {
            var snapshot = scope.Snapshot;
            if (snapshot is null || !snapshot.IsOpen)
                return snapshot;

            // Se elige rival con lo leído, sin bloquear. Después se bloquean las dos filas y se
            // vuelve a comprobar todo, porque otra instancia pudo cambiarlas mientras tanto.
            MatchSearch? candidate = null;
            if (snapshot.Status == MatchSearchStatus.Waiting && !snapshot.IsInactive(now))
                candidate = await scope.FindCandidateAsync(snapshot, now, cancellationToken);

            var ids = candidate is null ? new[] { searchId } : new[] { searchId, candidate.Id };
            var locked = await scope.LockAsync(ids, cancellationToken);
            search = locked.SingleOrDefault(item => item.Id == searchId);
            var rival = candidate is null ? null : locked.SingleOrDefault(item => item.Id == candidate.Id);
            if (search is null)
                return null;

            if (search.Status == MatchSearchStatus.Waiting)
            {
                if (search.IsInactive(now))
                {
                    // Dejó de consultar: queda fuera de la búsqueda y nunca se empareja (#128).
                    search.Expire(now);
                    await scope.CommitAsync(cancellationToken);
                    return search;
                }

                if (markSeen)
                    search.Touch(now);

                // El rival sigue en espera y activo (pudo emparejarse, salir o vencer).
                if (rival is not null && rival.Status == MatchSearchStatus.Waiting && !rival.IsInactive(now))
                {
                    var pairingId = Guid.CreateVersion7();
                    search.BeginPairing(pairingId, now);
                    rival.BeginPairing(pairingId, now);
                    request = BuildRequest(pairingId, search, rival);
                }

                await scope.CommitAsync(cancellationToken);
            }
            else if (search.Status == MatchSearchStatus.Pairing)
            {
                if (markSeen)
                    search.Touch(now);

                // Otra solicitud ya está creando la partida. Solo se reintenta si el
                // emparejamiento lleva demasiado tiempo sin cerrarse.
                if (search.PairedAt is { } pairedAt && now - pairedAt >= MatchmakingRules.PairingRetryAfter)
                {
                    var partner = await scope.FindPartnerAsync(search, cancellationToken);
                    if (partner is null)
                    {
                        search.ReturnToWaiting("No se pudo completar el emparejamiento. Sigues en la búsqueda.");
                    }
                    else
                    {
                        search.BeginPairing(search.PairingId!.Value, now);
                        request = BuildRequest(search.PairingId.Value, search, partner);
                    }
                }

                await scope.CommitAsync(cancellationToken);
            }
            else
            {
                // Mientras se esperaba el bloqueo, la búsqueda encontró partida, se canceló o
                // expiró: no hay nada que hacer.
                return search;
            }
        }

        // Sin rival por ahora: la búsqueda queda como se guardó en el paso 1.
        if (request is null)
            return search;

        // PASOS 2 Y 3. Crear la partida y cerrar el emparejamiento. Después se vuelve a leer la
        // búsqueda, porque el paso 3 la cambió en la base de datos.
        await FinishPairingAsync(request, cancellationToken);
        return await searches.FindAsync(searchId, cancellationToken);
    }

    private async Task FinishPairingAsync(PairedMatchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await matchStarter.StartAsync(request, cancellationToken);
            if (outcome.MatchId is { } matchId)
            {
                await searches.CompletePairingAsync(request.PairingId, matchId, clock.GetUtcNow(), cancellationToken);
                logger.LogInformation(
                    "Emparejamiento {PairingId}: partida {MatchId} entre {Challenger} y {Opponent}.",
                    request.PairingId, matchId, request.ChallengerId, request.OpponentId);
            }
            else
            {
                var reason = outcome.FailureReason ?? "No se pudo crear la partida.";
                await searches.ReturnPairingToWaitingAsync(request.PairingId, reason, cancellationToken);
                logger.LogWarning(
                    "Emparejamiento {PairingId}: la partida no se creó ({Reason}). Ambos vuelven a la búsqueda.",
                    request.PairingId, reason);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Las dos búsquedas quedan en Pairing y se reintenta en una consulta posterior.
            logger.LogError(exception, "Emparejamiento {PairingId}: falla al crear la partida.", request.PairingId);
        }
    }

    // La búsqueda más antigua es la del desafiante. El orden es fijo para que un reintento
    // pida exactamente la misma partida.
    private static PairedMatchRequest BuildRequest(Guid pairingId, MatchSearch first, MatchSearch second)
    {
        var (challenger, opponent) =
            (first.CreatedAt, first.Id).CompareTo((second.CreatedAt, second.Id)) <= 0
                ? (first, second)
                : (second, first);

        return new PairedMatchRequest(
            pairingId,
            challenger.PlayerId,
            challenger.ReadDeckCardIds(),
            opponent.PlayerId,
            opponent.ReadDeckCardIds());
    }
}
