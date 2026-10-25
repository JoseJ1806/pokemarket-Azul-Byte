using Microsoft.Extensions.Logging.Abstractions;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain;
using PokeArena.Api.Modules.Locations.Domain.Effects;
using PokeArena.Api.Modules.Locations.Infrastructure;
using PokeArena.Api.Shared;

namespace PokeArena.Tests;

// Pruebas unitarias del caso de uso Crear Ubicación con dobles de prueba.
// No usan base de datos, disco ni imágenes reales.
public class CreateLocationUseCaseTests
{
    [Fact]
    public async Task ValidCommand_CreatesAvailableLocationWithBothImages()
    {
        var store = new FakeImageStore();
        var repository = new FakeLocationRepository();

        var result = await CreateUseCase(new FakeImageCodec(0), store, repository)
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.IsReplay);
        Assert.Equal("Lista", result.Location!.ImageStatus);
        Assert.True(result.Location.IsAvailable);
        Assert.NotNull(result.Location.ImageUrl);
        Assert.NotNull(result.Location.ThumbnailUrl);
        Assert.Equal(2, store.Files.Count);
        Assert.NotNull(repository.CommittedLocation);
    }

    [Fact]
    public async Task ThumbnailFailsEveryAttempt_LocationIsFailedAndNotAvailable()
    {
        var codec = new FakeImageCodec(failuresBeforeSuccess: 99);
        var store = new FakeImageStore();

        var result = await CreateUseCase(codec, store, new FakeLocationRepository())
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(ProcessImageUseCase.MaxThumbnailAttempts, codec.ThumbnailCalls);
        Assert.Equal("Fallida", result.Location!.ImageStatus);
        Assert.False(result.Location.IsAvailable);
        Assert.Null(result.Location.ThumbnailUrl);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task SameKeyAndSameData_ReturnsExistingWithoutProcessingTheImage()
    {
        var codec = new FakeImageCodec(0);
        var repository = new FakeLocationRepository
        {
            ExistingLocation = new Location
            {
                Id = 7,
                Name = "Gruta Volcánica",
                Description = "Una cueva caliente.",
                CardLimit = 3,
                EnergyMode = "Fija",
                EnergyAmount = 2
            }
        };

        var result = await CreateUseCase(codec, new FakeImageStore(), repository)
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.True(result.IsReplay);
        Assert.Equal(7, result.Location!.Id);
        Assert.Equal(0, codec.ThumbnailCalls);
        Assert.False(repository.ScopeOpened);
    }

    [Fact]
    public async Task SameKeyWithDifferentData_IsRejectedAsConflict()
    {
        var repository = new FakeLocationRepository
        {
            ExistingLocation = new Location
            {
                Id = 7,
                Name = "Otra ubicación",
                Description = "Otra descripción.",
                CardLimit = 1,
                EnergyMode = "Fija",
                EnergyAmount = 0
            }
        };

        var result = await CreateUseCase(new FakeImageCodec(0), new FakeImageStore(), repository)
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(DomainErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("idempotency-key-conflict", result.Error.Code);
    }

    [Fact]
    public async Task EffectIsStoredNormalized()
    {
        var command = ValidCommand() with
        {
            EffectType = "RestriccionPorTurno",
            EffectParameters = "{\"turnos\":[3,1]}"
        };

        var result = await CreateUseCase(new FakeImageCodec(0), new FakeImageStore(), new FakeLocationRepository())
            .ExecuteAsync(command, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("RestriccionPorTurno", result.Location!.EffectType);
        Assert.Equal("{\"turnos\":[1,3]}", result.Location.EffectParameters);
    }

    [Fact]
    public async Task NoEffect_IsAllowed()
    {
        var result = await CreateUseCase(new FakeImageCodec(0), new FakeImageStore(), new FakeLocationRepository())
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Null(result.Location!.EffectType);
        Assert.Null(result.Location.EffectParameters);
    }

    [Fact]
    public async Task InvalidEffect_IsRejectedAndNothingIsSaved()
    {
        var store = new FakeImageStore();
        var repository = new FakeLocationRepository();
        var command = ValidCommand() with
        {
            EffectType = "BonificacionPorTipo",
            EffectParameters = "{\"tipo\":\"Dragón\",\"cantidad\":3}"
        };

        var result = await CreateUseCase(new FakeImageCodec(0), store, repository)
            .ExecuteAsync(command, CancellationToken.None);

        Assert.Equal("effectParameters", result.Error!.Field);
        Assert.Empty(store.Files);
        Assert.Null(repository.CommittedLocation);
    }

    [Theory]
    [InlineData("name", "required-field")]
    [InlineData("description", "required-field")]
    [InlineData("cardLimit", "required-field")]
    [InlineData("energyMode", "required-field")]
    [InlineData("energyAmount", "required-field")]
    [InlineData("image", "required-field")]
    public async Task MissingRequiredField_IsRejectedIndicatingTheField(string field, string code)
    {
        var command = field switch
        {
            "name" => ValidCommand() with { Name = null },
            "description" => ValidCommand() with { Description = " " },
            "cardLimit" => ValidCommand() with { CardLimit = null },
            "energyMode" => ValidCommand() with { EnergyMode = null },
            "energyAmount" => ValidCommand() with { EnergyAmount = null },
            _ => ValidCommand() with { Image = null }
        };

        var result = await CreateUseCase(new FakeImageCodec(0), new FakeImageStore(), new FakeLocationRepository())
            .ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(field, result.Error.Field);
    }

    [Theory]
    [InlineData(6, "Fija", 0, "cardLimit")]
    [InlineData(3, "Inventada", 0, "energyMode")]
    [InlineData(3, "Fija", 4, "energyAmount")]
    [InlineData(3, "PorCartas", 3, "energyAmount")]
    public async Task InvalidSettings_AreRejectedAndNothingIsSaved(int limit, string mode, int amount, string field)
    {
        var store = new FakeImageStore();
        var repository = new FakeLocationRepository();
        var command = ValidCommand() with { CardLimit = limit, EnergyMode = mode, EnergyAmount = amount };

        var result = await CreateUseCase(new FakeImageCodec(0), store, repository)
            .ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(field, result.Error!.Field);
        Assert.Empty(store.Files);
        Assert.Null(repository.CommittedLocation);
    }

    [Fact]
    public async Task ImageWithWrongRatio_IsRejectedAndNothingIsSaved()
    {
        var codec = new FakeImageCodec(0) { Dimensions = new ImageDimensions(600, 840) };
        var store = new FakeImageStore();
        var repository = new FakeLocationRepository();

        var result = await CreateUseCase(codec, store, repository)
            .ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("invalid-image", result.Error!.Code);
        Assert.Empty(store.Files);
        Assert.Null(repository.CommittedLocation);
    }

    [Fact]
    public async Task CommitFailure_DeletesTheStoredFiles()
    {
        var store = new FakeImageStore();
        var repository = new FakeLocationRepository { FailOnCommit = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateUseCase(new FakeImageCodec(0), store, repository)
                .ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Empty(store.Files);
    }

    private static CreateLocationUseCase CreateUseCase(
        FakeImageCodec codec,
        FakeImageStore store,
        FakeLocationRepository repository)
    {
        var images = new ProcessImageUseCase(codec, store, NullLogger<ProcessImageUseCase>.Instance);
        var types = new CatalogCardTypeLookup();
        var effects = new LocationEffectCatalog(new ILocationEffectType[]
        {
            new TypeBonusEffect(types),
            new CostModifierEffect(types),
            new TurnRestrictionEffect()
        });
        return new CreateLocationUseCase(repository, images, effects);
    }

    private static CreateLocationCommand ValidCommand() => new(
        "key-1",
        "Gruta Volcánica",
        "Una cueva caliente.",
        3,
        "Fija",
        2,
        null,
        null,
        new ImageUpload("image/png", 3, [1, 2, 3]));

    private sealed class FakeImageCodec(int failuresBeforeSuccess) : IImageCodec
    {
        public int ThumbnailCalls { get; private set; }

        public ImageDimensions? Dimensions { get; set; } = new(800, 600);

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

    private sealed class FakeLocationRepository : ILocationRepository
    {
        public Location? ExistingLocation { get; set; }

        public bool FailOnCommit { get; set; }

        public bool ScopeOpened { get; private set; }

        public Location? CommittedLocation { get; private set; }

        public Task<Location?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingLocation);

        public Task<ILocationCreationScope> BeginCreationAsync(
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            ScopeOpened = true;
            return Task.FromResult<ILocationCreationScope>(new FakeScope(this));
        }

        private sealed class FakeScope(FakeLocationRepository owner) : ILocationCreationScope
        {
            public Task<Location?> FindExistingAsync(CancellationToken cancellationToken) =>
                Task.FromResult<Location?>(null);

            public Task CommitAsync(Location location, CancellationToken cancellationToken)
            {
                if (owner.FailOnCommit)
                    throw new InvalidOperationException("Fallo simulado al guardar.");

                owner.CommittedLocation = location;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}