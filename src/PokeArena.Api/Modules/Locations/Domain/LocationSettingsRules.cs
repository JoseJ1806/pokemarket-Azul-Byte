using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Domain;

// VALIDACIÓN DE LA CONFIGURACIÓN DE UNA UBICACIÓN: límite de cartas y energía extra.
// No depende de HTTP, EF ni archivos. Devuelve null cuando todo es válido.
public static class LocationSettingsRules
{
    public static DomainError? Validate(int cardLimit, string? energyMode, int energyAmount)
    {
        if (cardLimit is < LocationRules.MinCardLimit or > LocationRules.MaxCardLimit)
            return Invalid("cardLimit debe estar entre 0 y 5.", "cardLimit");

        int maxEnergy;
        switch (energyMode)
        {
            case LocationRules.EnergyModeFixed:
                maxEnergy = LocationRules.MaxFixedEnergy;
                break;
            case LocationRules.EnergyModePerCard:
                maxEnergy = LocationRules.MaxPerCardEnergy;
                break;
            default:
                return Invalid("energyMode debe ser Fija o PorCartas.", "energyMode");
        }

        if (energyAmount < LocationRules.MinEnergy || energyAmount > maxEnergy)
            return Invalid(
                $"energyAmount debe estar entre 0 y {maxEnergy} para la modalidad {energyMode}.",
                "energyAmount");

        return null;
    }

    private static DomainError Invalid(string message, string field) =>
        DomainError.Validation("invalid-field", message, field);
}