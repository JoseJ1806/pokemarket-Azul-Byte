using PokeArena.Api.Models;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Application;

// Resultado de consultar una carta: la carta, su habilidad completa (si tiene) o un error.
public sealed record GetCardResult(Card? Card, Ability? Ability, DomainError? Error)
{
    public static GetCardResult Found(Card card, Ability? ability) => new(card, ability, null);

    public static GetCardResult Failed(DomainError error) => new(null, null, error);
}

// CASO DE USO Consultar ficha de carta.
// Devuelve la carta con los datos de su habilidad (nombre, evento, efecto, tipo afectado
// y carta destino si es de evolución). Las cartas con imagen Fallida también se pueden
// consultar, porque el administrador necesita ver el estado de la imagen.
public sealed class GetCardUseCase(ICardRepository cards)
{
    public async Task<GetCardResult> ExecuteAsync(int id, CancellationToken cancellationToken)
    {
        var card = await cards.FindByIdAsync(id, cancellationToken);
        if (card is null)
        {
            return GetCardResult.Failed(new DomainError(
                DomainErrorKind.NotFound,
                "card-not-found",
                $"No existe una carta con id {id}."));
        }

        Ability? ability = null;
        if (!string.IsNullOrWhiteSpace(card.AbilityCode))
            ability = await cards.FindAbilityAsync(card.AbilityCode, cancellationToken);

        return GetCardResult.Found(card, ability);
    }
}
