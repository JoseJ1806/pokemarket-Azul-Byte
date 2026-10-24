using PokeArena.Api.Modules.Catalog.Domain;
using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain.Effects;
using PokeArena.Api.Modules.Locations.Infrastructure;

namespace PokeArena.Tests;

// Pruebas unitarias del catálogo de efectos de ubicación. No usan base de datos ni HTTP.
public class LocationEffectCatalogTests
{
    private static LocationEffectCatalog CreateCatalog()
    {
        var types = new CatalogCardTypeLookup();
        return new LocationEffectCatalog(new ILocationEffectType[]
        {
            new TypeBonusEffect(types),
            new CostModifierEffect(types),
            new TurnRestrictionEffect()
        });
    }

    [Fact]
    public void NoEffectAndNoParameters_IsValid()
    {
        var result = CreateCatalog().Validate(null, null);

        Assert.Null(result.Error);
        Assert.Null(result.NormalizedParameters);
    }

    [Fact]
    public void UnknownEffectType_IsRejected()
    {
        var result = CreateCatalog().Validate("Inventado", "{}");

        Assert.Equal("invalid-effect-type", result.Error!.Code);
        Assert.Equal("effectType", result.Error.Field);
    }

    [Theory]
    [InlineData("{\"tipo\":\"Fuego\",\"cantidad\":5}")]
    [InlineData("{\"tipo\":\"Agua\",\"cantidad\":-5}")]
    public void TypeBonus_ValidParameters_AreAccepted(string json)
    {
        Assert.Null(CreateCatalog().Validate("BonificacionPorTipo", json).Error);
    }

    [Theory]
    [InlineData("{\"cantidad\":3}")]
    [InlineData("{\"tipo\":\"Dragón\",\"cantidad\":3}")]
    [InlineData("{\"tipo\":\"Fuego\",\"cantidad\":6}")]
    [InlineData("{\"tipo\":\"Fuego\",\"cantidad\":\"3\"}")]
    public void TypeBonus_InvalidParameters_AreRejected(string json)
    {
        Assert.NotNull(CreateCatalog().Validate("BonificacionPorTipo", json).Error);
    }

    [Fact]
    public void CostModifier_AffectedTypeIsOptional()
    {
        Assert.Null(CreateCatalog().Validate("ModificadorDeCosto", "{\"cantidad\":-2}").Error);
        Assert.Null(CreateCatalog().Validate("ModificadorDeCosto", "{\"cantidad\":2,\"tipoAfectado\":\"Agua\"}").Error);
    }

    [Fact]
    public void CostModifier_OutOfRangeOrUnknownType_IsRejected()
    {
        Assert.NotNull(CreateCatalog().Validate("ModificadorDeCosto", "{\"cantidad\":-6}").Error);
        Assert.NotNull(CreateCatalog().Validate("ModificadorDeCosto", "{\"cantidad\":1,\"tipoAfectado\":\"Dragón\"}").Error);
    }

    [Theory]
    [InlineData("{\"turnos\":[]}")]
    [InlineData("{\"turnos\":[0]}")]
    [InlineData("{\"turnos\":[7]}")]
    [InlineData("{\"turnos\":[2,2]}")]
    [InlineData("{\"turnos\":\"1\"}")]
    public void TurnRestriction_InvalidTurns_AreRejected(string json)
    {
        Assert.NotNull(CreateCatalog().Validate("RestriccionPorTurno", json).Error);
    }

    [Fact]
    public void TurnRestriction_ValidTurns_AreNormalizedAndSorted()
    {
        var result = CreateCatalog().Validate("RestriccionPorTurno", "{\"turnos\":[3,1]}");

        Assert.Null(result.Error);
        Assert.Equal("{\"turnos\":[1,3]}", result.NormalizedParameters);
    }

    [Fact]
    public void ParametersWithoutEffectType_AreRejected()
    {
        Assert.NotNull(CreateCatalog().Validate(null, "{\"cantidad\":1}").Error);
    }

    [Fact]
    public void InvalidJson_IsRejected()
    {
        Assert.NotNull(CreateCatalog().Validate("RestriccionPorTurno", "no es json").Error);
    }
}