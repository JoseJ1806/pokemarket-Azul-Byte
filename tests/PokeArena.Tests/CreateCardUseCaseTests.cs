using Microsoft.Extensions.Logging.Abstractions;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Images.Application;

namespace PokeArena.Tests;

// Pruebas unitarias del caso de uso Crear Carta con dobles de prueba.
// No usan base de datos, disco ni imágenes reales: ejercitan los criterios de fallo del ADR-002.
public class CreateCardUseCaseTests
{
    [Fact]
    public async Task ThumbnailFailsTwiceThenWorks_CardIsReadyAndAvailable()
    {
        var codec = new FakeImageCodec(failuresBeforeSuccess: 2);
        var store = new FakeImageStore();
        var repository = new FakeCardRepository();

        var result = await CreateUseCase(codec, store, repository).ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(3, codec.ThumbnailCalls);
        Assert.Equal("Lista", result.Card!.ImageStatus);
        Assert.True(result.Card.IsAvailable);
        Assert.NotNull(result.Card.ThumbnailUrl);
        Assert.Equal(2, store.Files.Count);
    }

    [Fact]
    public async Task ThumbnailFailsEveryAttempt_CardIsFailedAndNotAvailable()
    {
        var codec = new FakeImageCodec(failuresBeforeSuccess: int.MaxValue);
        var store = new FakeImageStore();
        var repository = new FakeCardRepository();

        var result = await CreateUseCase(codec, store, repository).ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(ProcessImageUseCase.MaxThumbnailAttempts, codec.ThumbnailCalls);
        Assert.Equal("Fallida", result.Card!.ImageStatus);
        Assert.False(result.Card.IsAvailable);
        Assert.Null(result.Card.ThumbnailUrl);
        Assert.NotNull(result.Card.ImageUrl);
        Assert.Single(store.Files);
        Assert.NotNull(repository.CommittedCard);
    }

    [Fact]
    public async Task ThumbnailWorksFirstTime_UsesOnlyOneAttempt()
    {
        var codec = new FakeImageCodec(failuresBeforeSuccess: 0);

        var result = await CreateUseCase(codec, new FakeImageStore(), new FakeCardRepository())
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1, codec.ThumbnailCalls);
    }

    [Fact]
    public async Task SavingTheCardFails_DeletesTheStoredFilesAndRethrows()
    {
        var store = new FakeImageStore();
        var repository = new FakeCardRepository { FailOnCommit = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateUseCase(new FakeImageCodec(0), store, repository).ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Empty(store.Files);
        Assert.Null(repository.CommittedCard);
    }

    [Fact]
    public async Task InvalidImage_DoesNotTouchStoreOrRepositoryScope()
    {
        var codec = new FakeImageCodec(0) { Dimensions = new ImageDimensions(700, 700) };
        var store = new FakeImageStore();
        var repository = new FakeCardRepository();

        var result = await CreateUseCase(codec, store, repository).ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("invalid-image", result.Error!.Code);
        Assert.Empty(store.Files);
        Assert.False(repository.ScopeOpened);
        Assert.Equal(0, codec.ThumbnailCalls);
    }

    [Fact]
    public async Task ExistingKeyWithSameData_IsReplayWithoutProcessingImage()
    {
        var codec = new FakeImageCodec(0);
        var store = new FakeImageStore();
        var repository = new FakeCardRepository
        {
            ExistingCard = new Card
            {
                Id = 7, Code = "PKA-AGU-001", Name = "Carta", Cost = 3, Power = 5,
                Type = "Agua", Rarity = "Común", AbilityCode = "HAB-000", IdempotencyKey = "key-1"
            }
        };

        var result = await CreateUseCase(codec, store, repository).ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsReplay);
        Assert.Equal(7, result.Card!.Id);
        Assert.Equal(0, codec.ThumbnailCalls);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task ExistingKeyWithDifferentData_IsConflict()
    {
        var repository = new FakeCardRepository
        {
            ExistingCard = new Card
            {
                Id = 7, Code = "PKA-AGU-001", Name = "Otro nombre", Cost = 3, Power = 5,
                Type = "Agua", Rarity = "Común", AbilityCode = "HAB-000", IdempotencyKey = "key-1"
            }
        };

        var result = await CreateUseCase(new FakeImageCodec(0), new FakeImageStore(), repository)
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("idempotency-key-conflict", result.Error!.Code);
        Assert.Null(result.Card);
    }

    private static CreateCardUseCase CreateUseCase(
        FakeImageCodec codec,
        FakeImageStore store,
        FakeCardRepository repository) =>
        new(repository, new ProcessImageUseCase(codec, store, NullLogger<ProcessImageUseCase>.Instance));

    private static CreateCardCommand ValidCommand() => new(
        "key-1",
        "Carta",
        3,
        5,
        "Agua",
        "Común",
        "HAB-000",
        new ImageUpload("image/png", 10, new byte[10]));

    private sealed class FakeImageCodec(int failuresBeforeSuccess) : IImageCodec
    {
        public int ThumbnailCalls { get; private set; }

        public ImageDimensions? Dimensions { get; set; } = new(600, 840);

        public ImageDimensions? Inspect(byte[] content) => Dimensions;

        public byte[] CreateThumbnail(byte[] content, int width, int height)
        {
            ThumbnailCalls++;
            if (ThumbnailCalls <= failuresBeforeSuccess)
                throw new InvalidOperationException("Fallo simulado al generar la miniatura.");

            return [1, 2, 3];
        }
    }

    private sealed class FakeImageStore : IImageStore
    {
        public HashSet<string> Files { get; } = [];

        public Task SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken)
        {
            Files.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            Files.Remove(relativePath);
            return Task.CompletedTask;
        }

        public string GetUrl(string relativePath) => "/" + relativePath;
    }

    private sealed class FakeCardRepository : ICardRepository
    {
        public Card? ExistingCard { get; set; }

        public bool FailOnCommit { get; set; }

        public bool ScopeOpened { get; private set; }

        public Card? CommittedCard { get; private set; }

        public Task<Card?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingCard);

        public Task<Card?> FindByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<Card?>(null);

        public Task<Ability?> FindAbilityAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<Ability?>(new Ability { Code = code, Name = "Sin Habilidad" });

        public Task<ICardCreationScope> BeginCreationAsync(
            string idempotencyKey,
            string typePrefix,
            CancellationToken cancellationToken)
        {
            ScopeOpened = true;
            return Task.FromResult<ICardCreationScope>(new FakeScope(this));
        }

        private sealed class FakeScope(FakeCardRepository owner) : ICardCreationScope
        {
            public Task<Card?> FindExistingAsync(CancellationToken cancellationToken) =>
                Task.FromResult<Card?>(null);

            public Task<string> ReserveNextCodeAsync(CancellationToken cancellationToken) =>
                Task.FromResult("PKA-AGU-001");

            public Task<bool> EvolutionCreatesCycleAsync(
                string newCardCode,
                string targetCardCode,
                CancellationToken cancellationToken) => Task.FromResult(false);

            public Task CommitAsync(Card card, CancellationToken cancellationToken)
            {
                if (owner.FailOnCommit)
                    throw new InvalidOperationException("Fallo simulado al guardar la carta.");

                owner.CommittedCard = card;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
