using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Tests;

// PRUEBAS UNITARIAS de las reglas y de la búsqueda (módulo Emparejamiento).
// No tocan la base de datos: prueban los números del ADR-007 y las transiciones del modelo de
// estado "Búsqueda de partida" de la V3.
public class MatchmakingRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // RF-EMP-02: la diferencia aceptada empieza en 6, suma 3 cada 5 segundos y no pasa de 30.
    [Theory]
    [InlineData(0, 6)]
    [InlineData(4.9, 6)]
    [InlineData(5, 9)]
    [InlineData(24.9, 18)]
    [InlineData(25, 21)]
    [InlineData(40, 30)]
    [InlineData(600, 30)]
    public void EloRange_WidensWithWaitingTime_UpToTheMaximum(double seconds, int expected)
    {
        Assert.Equal(expected, MatchmakingRules.EloRangeFor(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void EloRange_NeverGoesBelowTheInitialRange()
    {
        Assert.Equal(MatchmakingRules.InitialEloRange, MatchmakingRules.EloRangeFor(TimeSpan.FromSeconds(-3)));
    }

    // RN-14: exactamente 12 cartas distintas que el jugador posee.
    [Theory]
    [InlineData(12, 12, 12, null)]
    [InlineData(11, 11, 11, "El deck debe tener exactamente 12 cartas y tiene 11.")]
    [InlineData(12, 11, 11, "El deck tiene cartas repetidas.")]
    [InlineData(12, 12, 10, "El deck tiene cartas que ya no están en tu colección.")]
    public void DeckProblem_ExplainsWhyADeckCannotBeUsed(int cards, int distinct, int owned, string? expected)
    {
        Assert.Equal(expected, MatchmakingRules.DeckProblem(cards, distinct, owned));
    }

    [Fact]
    public void Open_StartsWaitingWithTheDeckCopiedInOrder()
    {
        var search = NewSearch();

        Assert.Equal(MatchSearchStatus.Waiting, search.Status);
        Assert.True(search.IsOpen);
        Assert.Equal(T0, search.LastSeenAt);
        Assert.Equal(Enumerable.Range(1, 12), search.ReadDeckCardIds());
    }

    // #128: un jugador que deja de consultar más de 20 segundos queda fuera de la búsqueda.
    [Fact]
    public void IsInactive_OnlyAfterTheInactivityTimeout()
    {
        var search = NewSearch();

        Assert.False(search.IsInactive(T0 + MatchmakingRules.InactivityTimeout));
        Assert.True(search.IsInactive(T0 + MatchmakingRules.InactivityTimeout + TimeSpan.FromSeconds(1)));

        search.Touch(T0 + TimeSpan.FromSeconds(15));
        Assert.False(search.IsInactive(T0 + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Cancel_ClosesTheSearch()
    {
        var search = NewSearch();

        search.Cancel(T0 + TimeSpan.FromSeconds(7));

        Assert.Equal(MatchSearchStatus.Cancelled, search.Status);
        Assert.False(search.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(7), search.WaitedAt(T0 + TimeSpan.FromMinutes(5)));
    }

    // Una búsqueda que ya se está emparejando no se puede cancelar ni expirar.
    [Fact]
    public void ASearchBeingPaired_CannotBeCancelledOrExpired()
    {
        var search = NewSearch();
        search.BeginPairing(Guid.NewGuid(), T0);

        Assert.Throws<InvalidOperationException>(() => search.Cancel(T0));
        Assert.Throws<InvalidOperationException>(() => search.Expire(T0));
        Assert.False(search.IsInactive(T0 + TimeSpan.FromMinutes(5)));
    }

    // Cancelada o expirada, una búsqueda nunca se empareja.
    [Fact]
    public void ClosedSearches_AreNeverPaired()
    {
        var cancelled = NewSearch();
        cancelled.Cancel(T0);
        var expired = NewSearch();
        expired.Expire(T0);

        Assert.Throws<InvalidOperationException>(() => cancelled.BeginPairing(Guid.NewGuid(), T0));
        Assert.Throws<InvalidOperationException>(() => expired.BeginPairing(Guid.NewGuid(), T0));
    }

    [Fact]
    public void ReturnToWaiting_ClearsThePairingAndKeepsTheReason()
    {
        var search = NewSearch();
        search.BeginPairing(Guid.NewGuid(), T0);

        search.ReturnToWaiting("No hay ubicaciones disponibles.");

        Assert.Equal(MatchSearchStatus.Waiting, search.Status);
        Assert.Null(search.PairingId);
        Assert.Null(search.PairedAt);
        Assert.Equal("No hay ubicaciones disponibles.", search.Notice);
    }

    private static MatchSearch NewSearch() =>
        MatchSearch.Open(
            Guid.NewGuid(),
            MatchmakingRules.RankedContext,
            Guid.NewGuid(),
            Enumerable.Range(1, 12).Reverse().ToList(),
            elo: 0,
            T0);
}
