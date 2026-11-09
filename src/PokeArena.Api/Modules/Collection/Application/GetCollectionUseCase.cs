using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Collection.Application;

// Una carta de la colección con cuántas copias tiene el jugador.
public sealed record CollectionCardView(CardSummary Card, int Copies);

// La colección completa del jugador: su saldo y sus cartas agrupadas.
public sealed record CollectionView(int Cubes, IReadOnlyList<CollectionCardView> Cards);

// Resultado de consultar la colección. Mismo estilo que los demás resultados de la API.
public sealed record GetCollectionResult(CollectionView? Collection, DomainError? Error);

// CASO DE USO: CONSULTAR MI COLECCIÓN (módulo Colección, capa de aplicación).
//
// Lo usa GET /api/me/collection. Igual que /api/me, la ruta NO recibe un Id: el jugador
// sale del token firmado, así que es imposible pedir la colección de otra persona
// (RNF-SEG-04).
//
// La respuesta viene AGRUPADA por carta (decisión 7): una fila por carta distinta con su
// cantidad de copias, que es lo que pide la historia de explorar la colección.
public sealed class GetCollectionUseCase(ICollectionRepository repository, ICardSummaryLookup cards)
{
    public const string NotAPlayerCode = "player-not-found";

    // subject es el claim "sub" del JWT de PokéArena: el Id de la cuenta del jugador.
    public async Task<GetCollectionResult> ExecuteAsync(string? subject, CancellationToken cancellationToken)
    {
        // El token de administrador de desarrollo tiene un "sub" que no es un Guid. Es un
        // token válido, pero no corresponde a un jugador: se responde 404.
        if (!Guid.TryParse(subject, out var playerId))
            return new GetCollectionResult(
                null,
                DomainError.NotFound(NotAPlayerCode, "La sesión actual no corresponde a una cuenta de jugador."));

        var counts = await repository.GetCopyCountsAsync(playerId, cancellationToken);
        var cubes = await repository.GetCubeBalanceAsync(playerId, cancellationToken);

        // Los datos de presentación (nombre, imagen) los entrega Catálogo. Si el jugador
        // todavía no tiene cartas, no hace falta preguntar.
        IReadOnlyList<CardSummary> summaries = counts.Count == 0
            ? Array.Empty<CardSummary>()
            : await cards.GetByIdsAsync(counts.Select(count => count.CardId).ToList(), cancellationToken);

        var byId = summaries.ToDictionary(summary => summary.Id);

        // Orden estable: por nombre y, a igualdad de nombre, por Id.
        var items = counts
            .Where(count => byId.ContainsKey(count.CardId))
            .Select(count => new CollectionCardView(byId[count.CardId], count.Copies))
            .OrderBy(view => view.Card.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(view => view.Card.Id)
            .ToList();

        return new GetCollectionResult(new CollectionView(cubes, items), null);
    }
}
