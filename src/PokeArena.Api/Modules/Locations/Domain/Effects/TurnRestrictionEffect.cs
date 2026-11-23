using System.Text.Json;

namespace PokeArena.Api.Modules.Locations.Domain.Effects;

// Restricción por turno: en esos turnos no se pueden jugar cartas aquí. Ej: {"turnos":[1,2]}
// Pendiente de confirmar con el profesor: hoy se permite restringir los 6 turnos.
public sealed class TurnRestrictionEffect : ILocationEffectType
{
    public const string EffectName = "RestriccionPorTurno";

    public string Name => EffectName;

    public EffectValidation Validate(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("turnos", out var list) || list.ValueKind != JsonValueKind.Array)
            return EffectValidation.Invalid("RestriccionPorTurno requiere turnos, una lista de números de turno.");

        var turns = new SortedSet<int>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetInt32(out var turn)
                || turn < 1
                || turn > LocationRules.TotalTurns)
                return EffectValidation.Invalid($"Cada turno debe ser un entero entre 1 y {LocationRules.TotalTurns}.");

            if (!turns.Add(turn))
                return EffectValidation.Invalid("turnos no puede repetir un turno.");
        }

        if (turns.Count == 0)
            return EffectValidation.Invalid("turnos debe incluir al menos un turno.");

        return EffectValidation.Valid(JsonSerializer.Serialize(new { turnos = turns }));
    }
}