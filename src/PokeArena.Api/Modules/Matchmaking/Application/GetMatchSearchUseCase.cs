namespace PokeArena.Api.Modules.Matchmaking.Application;

// CASO DE USO: CONSULTAR LA BÚSQUEDA (US #128, módulo Emparejamiento).
//
// Devuelve la búsqueda más reciente del jugador: en espera, partida encontrada (con su id),
// cancelada o expirada. Si sigue abierta, la consulta también:
// - renueva la última consulta del jugador (si deja de consultar, la búsqueda expira);
// - intenta emparejarla, con el rango de Elo que corresponde al tiempo de espera.
public sealed class GetMatchSearchUseCase(
    IMatchSearchRepository searches,
    MatchmakingPairing pairing,
    TimeProvider clock)
{
    public async Task<MatchSearchResult> ExecuteAsync(string? subject, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var playerId))
            return MatchSearchResult.Failed(MatchmakingErrors.NotAPlayer());

        var latest = await searches.FindLatestAsync(playerId, cancellationToken);
        if (latest is null)
            return MatchSearchResult.Failed(MatchmakingErrors.NoSearch());

        if (latest.IsOpen)
            latest = await pairing.ProcessAsync(latest.Id, markSeen: true, cancellationToken) ?? latest;

        return MatchSearchResult.Ok(MatchSearchView.From(latest, clock.GetUtcNow()));
    }
}
