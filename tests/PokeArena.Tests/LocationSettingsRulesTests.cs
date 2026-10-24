using PokeArena.Api.Modules.Locations.Domain;

namespace PokeArena.Tests;

// Pruebas unitarias de la validación del límite de cartas y la energía extra.
public class LocationSettingsRulesTests
{
    [Theory]
    [InlineData(0, "Fija", 0)]
    [InlineData(5, "Fija", 3)]
    [InlineData(3, "PorCartas", 2)]
    [InlineData(1, "PorCartas", 0)]
    public void ValidSettings_AreAccepted(int cardLimit, string mode, int amount)
    {
        Assert.Null(LocationSettingsRules.Validate(cardLimit, mode, amount));
    }

    [Theory]
    [InlineData(-1, "Fija", 0, "cardLimit")]
    [InlineData(6, "Fija", 0, "cardLimit")]
    [InlineData(3, "Inventada", 1, "energyMode")]
    [InlineData(3, null, 1, "energyMode")]
    [InlineData(3, "fija", 1, "energyMode")]
    [InlineData(3, "Fija", 4, "energyAmount")]
    [InlineData(3, "Fija", -1, "energyAmount")]
    [InlineData(3, "PorCartas", 3, "energyAmount")]
    public void InvalidSettings_AreRejectedIndicatingTheField(int cardLimit, string? mode, int amount, string field)
    {
        var error = LocationSettingsRules.Validate(cardLimit, mode, amount);

        Assert.NotNull(error);
        Assert.Equal("invalid-field", error!.Code);
        Assert.Equal(field, error.Field);
    }
}