using Microsoft.Extensions.Logging;
using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Api.Modules.Matchmaking.Application;

// CASO DE USO: SALIR DE LA BÚSQUEDA (US #128, módulo Emparejamiento).
//
// Con el candado del jugador se bloquea su búsqueda abierta. El emparejamiento bloquea la misma
// fila, así que salir y emparejar nunca se mezclan (#128):
// - si la búsqueda sigue en espera, queda cancelada y ya nunca se empareja;
// - si ya se eligió rival (se está creando la partida) o ya tiene partida, la salida se
//   rechaza e indica la partida asignada.
public sealed class CancelMatchSearchUseCase(
    IMatchSearchRepository searches,
    TimeProvider clock,
    ILogger<CancelMatchSearchUseCase> logger)
{
    public async Task<MatchSearchResult> ExecuteAsync(string? subject, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var playerId))
            return MatchSearchResult.Failed(MatchmakingErrors.NotAPlayer());

        var now = clock.GetUtcNow();
        await using var scope = await searches.BeginPlayerScopeAsync(playerId, cancellationToken);

        var open = await scope.FindOpenAsync(cancellationToken);
        if (open is null)
        {
            // Sin búsqueda abierta. Si la última ya encontró partida, se dice cuál.
            var latest = await searches.FindLatestAsync(playerId, cancellationToken);
            if (latest is { Status: MatchSearchStatus.MatchFound, MatchId: { } matchId })
                return MatchSearchResult.Failed(MatchmakingErrors.Conflict(
                    "match-already-found",
                    $"Tu búsqueda ya encontró partida ({matchId}). No puedes salir de ella."));

            return MatchSearchResult.Failed(MatchmakingErrors.NoSearch());
        }

        if (open.Status == MatchSearchStatus.Pairing)
            return MatchSearchResult.Failed(MatchmakingErrors.Conflict(
                "match-being-created",
                "Ya se encontró un rival y se está creando tu partida. Consulta tu búsqueda para ver la " +
                "partida asignada."));

        // Si el jugador ya había dejado de consultar, la búsqueda estaba vencida: se registra
        // como expirada. Si no, queda cancelada.
        if (open.IsInactive(now))
            open.Expire(now);
        else
            open.Cancel(now);

        await scope.CommitAsync(cancellationToken);
        logger.LogInformation("El jugador {PlayerId} salió de la búsqueda {SearchId}.", playerId, open.Id);
        return MatchSearchResult.Ok(MatchSearchView.From(open, now));
    }
}
