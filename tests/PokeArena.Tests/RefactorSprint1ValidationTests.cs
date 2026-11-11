using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain.Effects;
using PokeArena.Api.Modules.Locations.Infrastructure;

namespace PokeArena.Tests;

// Pruebas unitarias de las piezas que salieron del refactor del Sprint 1 (tareas 216, 217 y 218):
// la regla de peso de ImageRequirements, los validadores separados del flujo y la comparación
// de los parámetros del efecto como JSON. No usan base de datos ni HTTP.
public class RefactorSprint1ValidationTests
{
    // 216: una sola regla del peso máximo de la imagen.
    [Theory]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    [InlineData(ImageRequirements.MaxFileSize, true)]
    [InlineData(ImageRequirements.MaxFileSize + 1, false)]
    public void ImageRequirements_IsWithinMaxFileSize(long length, bool expected)
    {
        Assert.Equal(expected, ImageRequirements.IsWithinMaxFileSize(length));
    }

    // 217: el validador de carta normaliza los datos sin tocar la base de datos.
    [Fact]
    public void CreateCardValidator_ValidCommand_ReturnsNormalizedRequest()
    {
        var result = CreateCardValidator.Validate(ValidCardCommand() with { Name = "  Charmander  ", AbilityCode = " HAB-000 " });

        Assert.Null(result.Error);
        Assert.Equal("Charmander", result.Value!.Name);
        Assert.Equal("FUE", result.Value.TypePrefix);
        Assert.Equal("HAB-000", result.Value.AbilityCode);
    }

    // 217: el orden de los errores se mantiene: la llave se revisa antes que el nombre.
    [Fact]
    public void CreateCardValidator_MissingKeyAndName_ReportsTheKeyFirst()
    {
        var result = CreateCardValidator.Validate(ValidCardCommand() with { IdempotencyKey = "", Name = null });

        Assert.Equal("missing-idempotency-key", result.Error!.Code);
        Assert.Equal("Idempotency-Key", result.Error.Field);
    }

    [Theory]
    [InlineData(11, 0, "cost")]
    [InlineData(0, 21, "power")]
    public void CreateCardValidator_OutOfRangeValues_IndicateTheField(int cost, int power, string field)
    {
        var result = CreateCardValidator.Validate(ValidCardCommand() with { Cost = cost, Power = power });

        Assert.Equal("invalid-field", result.Error!.Code);
        Assert.Equal(field, result.Error.Field);
    }

    [Fact]
    public void CreateCardValidator_EvolutionWithoutTarget_IsInvalid()
    {
        var error = CreateCardValidator.ValidateAbility(new Ability { Code = "HAB-010", IsEvolution = true });

        Assert.Equal("invalid-evolution", error!.Code);
    }

    [Fact]
    public void CreateCardValidator_AbilityWithoutEvolution_IsValid()
    {
        Assert.Null(CreateCardValidator.ValidateAbility(new Ability { Code = "HAB-000", Name = "Sin Habilidad" }));
    }

    // 217: el validador de ubicación normaliza los campos y el efecto.
    [Fact]
    public void CreateLocationValidator_ValidCommand_ReturnsNormalizedSettings()
    {
        var command = ValidLocationCommand() with
        {
            Name = "  Gruta Volcánica ",
            EffectType = "RestriccionPorTurno",
            EffectParameters = "{\"turnos\":[3,1]}"
        };

        var result = CreateLocationValidator.Validate(command, CreateEffectCatalog());

        Assert.Null(result.Error);
        Assert.Equal("Gruta Volcánica", result.Value!.Settings.Name);
        Assert.Equal("RestriccionPorTurno", result.Value.Settings.EffectType);
        Assert.Equal("{\"turnos\":[1,3]}", result.Value.Settings.EffectParameters);
    }

    [Fact]
    public void CreateLocationValidator_MissingImage_IsReportedAfterTheOtherFields()
    {
        var result = CreateLocationValidator.Validate(ValidLocationCommand() with { Image = null }, CreateEffectCatalog());

        Assert.Equal("required-field", result.Error!.Code);
        Assert.Equal("image", result.Error.Field);
    }

    // 218: el mismo JSON con otros espacios u otro orden de propiedades es equivalente,
    // que es como PostgreSQL devuelve una columna jsonb.
    [Theory]
    [InlineData("{\"turnos\":[1,3]}", "{\"turnos\": [1, 3]}")]
    [InlineData("{\"tipo\":\"Fuego\",\"cantidad\":2}", "{\"cantidad\": 2, \"tipo\": \"Fuego\"}")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void LocationEffectParameters_EquivalentJson_AreEqual(string? stored, string? requested)
    {
        Assert.True(LocationEffectParameters.AreEquivalent(stored, requested));
    }

    [Theory]
    [InlineData("{\"turnos\":[1,3]}", "{\"turnos\":[1,4]}")]
    [InlineData("{\"tipo\":\"Fuego\",\"cantidad\":2}", "{\"tipo\":\"Agua\",\"cantidad\":2}")]
    [InlineData("{\"turnos\":[1,3]}", null)]
    [InlineData("{\"turnos\":[1,3]}", "no es json")]
    public void LocationEffectParameters_DifferentJson_AreNotEqual(string? stored, string? requested)
    {
        Assert.False(LocationEffectParameters.AreEquivalent(stored, requested));
    }

    private static CreateCardCommand ValidCardCommand() => new(
        "key-1",
        "Charmander",
        3,
        5,
        "Fuego",
        "Común",
        "HAB-000",
        new ImageUpload("image/png", 3, [1, 2, 3]));

    private static CreateLocationCommand ValidLocationCommand() => new(
        "key-1",
        "Gruta Volcánica",
        "Una cueva caliente.",
        3,
        "Fija",
        2,
        null,
        null,
        new ImageUpload("image/png", 3, [1, 2, 3]));

    private static LocationEffectCatalog CreateEffectCatalog()
    {
        var types = new CatalogCardTypeLookup();
        return new LocationEffectCatalog(new ILocationEffectType[]
        {
            new TypeBonusEffect(types),
            new CostModifierEffect(types),
            new TurnRestrictionEffect()
        });
    }
}
