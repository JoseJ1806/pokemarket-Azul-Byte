using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Infrastructure;

namespace PokeArena.Tests;

// Pruebas del selector de las 15 cartas de la colección inicial.
// Se escriben ANTES que el código: al principio no compilan porque las clases no existen.
public class InitialCardSelectorTests
{
    // DOBLE de la fuente de azar: en vez de números impredecibles, responde lo que
    // nosotros le digamos. Gracias a esto el resultado se puede predecir EXACTAMENTE.
    // (Es la misma idea que FakeImageCodec en las pruebas de Crear carta.)
    private sealed class ScriptedRandomSource(Func<int, int> answer) : IRandomSource
    {
        public int Next(int maxExclusive) => answer(maxExclusive);
    }

    // Un azar que SIEMPRE responde 0, y otro que SIEMPRE responde el máximo permitido.
    private static InitialCardSelector SelectorWithAlwaysZero() =>
        new(new ScriptedRandomSource(_ => 0));

    private static InitialCardSelector SelectorWithAlwaysMax() =>
        new(new ScriptedRandomSource(max => max - 1));

    [Fact]
    public void ExactlyFifteenCards_ReturnsThoseFifteen()
    {
        var ids = Enumerable.Range(1, 15).ToList();

        var result = SelectorWithAlwaysZero().Select(ids);

        // Se compara como CONJUNTO: aquí importa qué cartas salen, no en qué orden.
        Assert.Equal(ids.OrderBy(id => id), result.OrderBy(id => id));
    }

    [Fact]
    public void MoreThanFifteenCards_ReturnsFifteenOfThem()
    {
        var ids = Enumerable.Range(1, 20).ToList();

        var result = new InitialCardSelector(new SystemRandomSource()).Select(ids);

        Assert.Equal(15, result.Count);
        Assert.All(result, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void Result_NeverRepeatsACard()
    {
        var ids = Enumerable.Range(1, 20).ToList();
        var selector = new InitialCardSelector(new SystemRandomSource());

        // El azar real cambia en cada ejecución, así que se repite muchas veces:
        // si hubiera un error de repetidas, tarde o temprano aparecería.
        for (var attempt = 0; attempt < 200; attempt++)
            Assert.Equal(15, selector.Select(ids).Distinct().Count());
    }

    [Fact]
    public void RandomSourceAlwaysZero_TakesTheFirstFifteenInOrder()
    {
        var ids = Enumerable.Range(100, 20).ToList(); // 100, 101, ..., 119

        var result = SelectorWithAlwaysZero().Select(ids);

        // Con azar "siempre 0" no se mueve nada: salen las primeras 15 en orden.
        Assert.Equal(Enumerable.Range(100, 15), result);
    }

    [Fact]
    public void RandomSourceAlwaysMax_GivesTheExpectedFifteen()
    {
        var ids = Enumerable.Range(100, 20).ToList();

        var result = SelectorWithAlwaysMax().Select(ids);

        // Valor calculado a mano con el mismo algoritmo: la última carta pasa al frente
        // y las demás se corren una posición.
        var expected = new List<int> { 119 }.Concat(Enumerable.Range(100, 14));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Selection_DoesNotModifyTheInputList()
    {
        var ids = Enumerable.Range(1, 20).ToList();
        var copy = ids.ToList();

        _ = new InitialCardSelector(new SystemRandomSource()).Select(ids);

        // El algoritmo intercambia elementos: debe hacerlo sobre una COPIA.
        Assert.Equal(copy, ids);
    }

    [Fact]
    public void DuplicatedIdsInTheInput_NeverProduceRepeatedCards()
    {
        // 15 cartas distintas, cada una repetida dos veces: 30 elementos en total.
        var ids = Enumerable.Range(1, 15).SelectMany(id => new[] { id, id }).ToList();

        var result = SelectorWithAlwaysZero().Select(ids);

        Assert.Equal(Enumerable.Range(1, 15), result.OrderBy(id => id));
    }

    [Fact]
    public void FewerThanFifteenDistinctCards_ThrowsBecauseTheCallerMustCheckFirst()
    {
        // 14 distintas (cada una repetida) no alcanzan. Es un error de quien llama:
        // el caso de uso debe responder NotAvailable ANTES de llegar al selector.
        var ids = Enumerable.Range(1, 14).SelectMany(id => new[] { id, id }).ToList();

        Assert.Throws<ArgumentException>(() => SelectorWithAlwaysZero().Select(ids));
    }
}