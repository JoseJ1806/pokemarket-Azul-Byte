using Microsoft.Extensions.Logging.Abstractions;
using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Domain;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Tests;

// PRUEBAS UNITARIAS de la entrega de la colección inicial y de la consulta de la colección.
//
// "Unitarias" significa que NO usan base de datos ni red: el repositorio, el catálogo y el
// azar se reemplazan por dobles (clases falsas al final del archivo). Por eso son rápidas
// y permiten comprobar cada situación del criterio de aceptación una por una.
public class GrantInitialCollectionUseCaseTests
{
    private static readonly Guid PlayerId = Guid.Parse("018f3a00-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static GrantInitialCollectionUseCase UseCase(FakeRepository repository, FakeCommonCards cards) =>
        new(
            repository,
            cards,
            new InitialCardSelector(new ZeroRandom()),
            new FixedTimeProvider(Now),
            NullLogger<GrantInitialCollectionUseCase>.Instance);

    // CRITERIO DE ÉXITO: con 15 o más Comunes disponibles se entregan 15 DISTINTAS y los
    // 1.000 cubos como PRIMER movimiento del historial, todo confirmado junto.
    [Fact]
    public async Task EnoughCommonCards_DeliversFifteenDistinctCopiesAndTheInitialCubes()
    {
        var repository = new FakeRepository();
        var cards = new FakeCommonCards(Enumerable.Range(1, 20).ToList());

        var outcome = await UseCase(repository, cards).GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.Granted, outcome);
        var scope = repository.Scope;
        Assert.True(scope.Committed);
        Assert.Equal(15, scope.Copies.Count);
        Assert.Equal(15, scope.Copies.Select(copy => copy.CardId).Distinct().Count());
        Assert.All(scope.Copies, copy =>
        {
            Assert.Equal(PlayerId, copy.PlayerId);
            Assert.Equal(CopyOrigin.Initial, copy.Origin);
            Assert.Equal(Now, copy.AcquiredAt);
        });
        var movement = Assert.Single(scope.Movements);
        Assert.Equal(1000, movement.Amount);
        Assert.Equal(CubeMovementReason.InitialGrant, movement.Reason);
        Assert.Equal(PlayerId, scope.Delivery!.PlayerId);
        Assert.True(scope.Disposed);
    }

    // Con exactamente 15 Comunes alcanza: se entregan todas.
    [Fact]
    public async Task ExactlyFifteenCommonCards_DeliversAllOfThem()
    {
        var repository = new FakeRepository();
        var ids = Enumerable.Range(100, 15).ToList();

        var outcome = await UseCase(repository, new FakeCommonCards(ids)).GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.Granted, outcome);
        Assert.Equal(ids, repository.Scope.Copies.Select(copy => copy.CardId).OrderBy(id => id));
    }

    // CRITERIO DE ÉXITO: ingresos posteriores NO vuelven a entregar. Ni siquiera se le
    // pregunta nada al catálogo.
    [Fact]
    public async Task AlreadyDelivered_ReturnsAlreadyGrantedWithoutTouchingTheCatalog()
    {
        var repository = new FakeRepository(alreadyDelivered: true);
        var cards = new FakeCommonCards(Enumerable.Range(1, 20).ToList());

        var outcome = await UseCase(repository, cards).GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.AlreadyGranted, outcome);
        Assert.Equal(0, cards.Calls);
        Assert.False(repository.Scope.Committed);
        Assert.Empty(repository.Scope.Copies);
        Assert.True(repository.Scope.Disposed);
    }

    // CRITERIO DE FALLO: con menos de 15 Comunes no se entrega nada y se avisa.
    [Fact]
    public async Task FewerThanFifteenCommonCards_ReturnsNotAvailableAndWritesNothing()
    {
        var repository = new FakeRepository();

        var outcome = await UseCase(repository, new FakeCommonCards(Enumerable.Range(1, 14).ToList()))
            .GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.NotAvailable, outcome);
        Assert.False(repository.Scope.Committed);
        Assert.Empty(repository.Scope.Copies);
        Assert.Empty(repository.Scope.Movements);
        Assert.Null(repository.Scope.Delivery);
        Assert.True(repository.Scope.Disposed);
    }

    // Identificadores repetidos no cuentan dos veces: 14 distintos, cada uno repetido, siguen
    // siendo 14.
    [Fact]
    public async Task DuplicatedIds_AreNotCountedTwice()
    {
        var repository = new FakeRepository();
        var ids = Enumerable.Range(1, 14).SelectMany(id => new[] { id, id }).ToList();

        var outcome = await UseCase(repository, new FakeCommonCards(ids)).GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.NotAvailable, outcome);
        Assert.False(repository.Scope.Committed);
    }

    // CRITERIO DE FALLO: si algo falla al guardar, el error sube (el login lo registra y deja
    // entrar al jugador) y NADA queda confirmado: la entrega es todo o nada.
    [Fact]
    public async Task FailureWhileCommitting_PropagatesAndNothingIsCommitted()
    {
        var repository = new FakeRepository();
        repository.Scope.FailOnCommit = new InvalidOperationException("la base de datos se cayó");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UseCase(repository, new FakeCommonCards(Enumerable.Range(1, 20).ToList()))
                .GrantIfMissingAsync(PlayerId, CancellationToken.None));

        Assert.False(repository.Scope.Committed);
        Assert.True(repository.Scope.Disposed);
    }

    // ÚLTIMA BARRERA: si la base de datos rechaza la entrega porque otra solicitud ya la hizo,
    // se interpreta como "ya estaba entregada", no como un error.
    [Fact]
    public async Task LosingTheRaceOnTheDatabase_IsReportedAsAlreadyGranted()
    {
        var repository = new FakeRepository();
        repository.Scope.FailOnCommit = new InitialDeliveryAlreadyExistsException(PlayerId, new Exception("23505"));

        var outcome = await UseCase(repository, new FakeCommonCards(Enumerable.Range(1, 20).ToList()))
            .GrantIfMissingAsync(PlayerId, CancellationToken.None);

        Assert.Equal(InitialCollectionOutcome.AlreadyGranted, outcome);
        Assert.False(repository.Scope.Committed);
    }

    // ---------------------------------------------------------------- DOBLES ----

    // Azar que siempre responde 0: el selector toma las primeras 15 y el resultado es predecible.
    private sealed class ZeroRandom : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
    }

    // Reloj que siempre dice la misma hora.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Catálogo falso: devuelve la lista que se le diga y cuenta cuántas veces le preguntaron.
    private sealed class FakeCommonCards(IReadOnlyList<int> ids) : ICommonCardSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<int>> GetAvailableCommonCardIdsAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ids);
        }
    }

    // Repositorio falso: entrega siempre el mismo alcance falso, que anota todo lo que le piden.
    private sealed class FakeRepository(bool alreadyDelivered = false) : ICollectionRepository
    {
        public FakeScope Scope { get; } = new(alreadyDelivered);

        public Task<IInitialDeliveryScope> BeginDeliveryAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.FromResult<IInitialDeliveryScope>(Scope);

        public Task<IReadOnlyList<CardCopyCount>> GetCopyCountsAsync(Guid playerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> GetCubeBalanceAsync(Guid playerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeScope(bool alreadyDelivered) : IInitialDeliveryScope
    {
        public InitialDelivery? Delivery { get; private set; }
        public List<CardCopy> Copies { get; } = [];
        public List<CubeMovement> Movements { get; } = [];
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? FailOnCommit { get; set; }

        public Task<bool> HasInitialDeliveryAsync(CancellationToken cancellationToken) => Task.FromResult(alreadyDelivered);

        public void AddInitialDelivery(InitialDelivery delivery) => Delivery = delivery;

        public void AddCopies(IReadOnlyList<CardCopy> copies) => Copies.AddRange(copies);

        public void AddCubeMovement(CubeMovement movement) => Movements.Add(movement);

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (FailOnCommit is not null)
                throw FailOnCommit;
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

// PRUEBAS UNITARIAS de la consulta de la colección (GET /api/me/collection).
public class GetCollectionUseCaseTests
{
    private static readonly Guid PlayerId = Guid.Parse("018f3a00-0000-7000-8000-000000000002");

    // Un token de administrador de desarrollo tiene un "sub" que no es un Guid: es válido
    // pero no es un jugador, así que se responde 404 y no se consulta nada.
    [Theory]
    [InlineData("admin-dev")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SubjectThatIsNotAPlayerId_ReturnsNotFound(string? subject)
    {
        var useCase = new GetCollectionUseCase(new FakeCollection([], 0), new FakeSummaries([]));

        var result = await useCase.ExecuteAsync(subject, CancellationToken.None);

        Assert.Null(result.Collection);
        Assert.Equal(DomainErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal(GetCollectionUseCase.NotAPlayerCode, result.Error.Code);
    }

    // CRITERIO: la respuesta viene AGRUPADA por carta, con su cantidad de copias y el saldo.
    [Fact]
    public async Task Collection_IsGroupedByCardWithCopiesAndCubes()
    {
        var useCase = new GetCollectionUseCase(
            new FakeCollection([new CardCopyCount(2, 3), new CardCopyCount(1, 1)], 1000),
            new FakeSummaries([Summary(1, "Bulbasaur"), Summary(2, "Abra")]));

        var result = await useCase.ExecuteAsync(PlayerId.ToString(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1000, result.Collection!.Cubes);
        // Orden por nombre: Abra antes que Bulbasaur.
        Assert.Equal(new[] { "Abra", "Bulbasaur" }, result.Collection.Cards.Select(view => view.Card.Name));
        Assert.Equal(new[] { 3, 1 }, result.Collection.Cards.Select(view => view.Copies));
    }

    // Un jugador sin entrega recibe una colección vacía y 0 cubos, no un error.
    [Fact]
    public async Task PlayerWithoutDelivery_GetsAnEmptyCollection()
    {
        var summaries = new FakeSummaries([]);
        var useCase = new GetCollectionUseCase(new FakeCollection([], 0), summaries);

        var result = await useCase.ExecuteAsync(PlayerId.ToString(), CancellationToken.None);

        Assert.Equal(0, result.Collection!.Cubes);
        Assert.Empty(result.Collection.Cards);
        // Sin cartas no hace falta preguntarle nada al catálogo.
        Assert.Equal(0, summaries.Calls);
    }

    private static CardSummary Summary(int id, string name) =>
        new(id, $"PKA-{id:000}", name, "Fuego", "Común", null, null);

    private sealed class FakeCollection(IReadOnlyList<CardCopyCount> counts, int cubes) : ICollectionRepository
    {
        public Task<IInitialDeliveryScope> BeginDeliveryAsync(Guid playerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CardCopyCount>> GetCopyCountsAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.FromResult(counts);

        public Task<int> GetCubeBalanceAsync(Guid playerId, CancellationToken cancellationToken) => Task.FromResult(cubes);
    }

    private sealed class FakeSummaries(IReadOnlyList<CardSummary> summaries) : ICardSummaryLookup
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<CardSummary>> GetByIdsAsync(
            IReadOnlyCollection<int> cardIds,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<CardSummary>>(summaries.Where(summary => cardIds.Contains(summary.Id)).ToList());
        }
    }
}
