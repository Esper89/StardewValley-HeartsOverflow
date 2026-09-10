using System.Numerics;
using StardewModdingAPI;
using StardewValley;

namespace HeartsOverflow;

public sealed class Api : IHeartsOverflowApi {
    internal Api(Mod mod) {
        this.mod = mod;
    }

    Mod mod;

    public BigInteger GetNpcTotalHearts(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetTotalNpcHearts(player, npc) : 0;

    public BigInteger GetNpcTotalFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetTotalNpcPoints(player, npc) : 0;

    public BigInteger GetNpcOverflowHearts(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetOverflowNpcHearts(player, npc) : 0;

    public BigInteger GetNpcOverflowFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetOverflowNpcPoints(player, npc) : 0;

    public void ClearNpcOverflowFriendship(Farmer player, NPC npc) {
        if (player is not null && npc is not null) this.mod.ClearNpcOverflow(player, npc);
    }

    public BigInteger GetAnimalTotalHearts(Character animal)
        => animal is not null ? this.mod.GetTotalAnimalHearts(animal) : 0;

    public BigInteger GetAnimalTotalFriendshipPoints(Character animal)
        => animal is not null ? this.mod.GetTotalAnimalPoints(animal) : 0;

    public BigInteger GetAnimalOverflowHearts(Character animal)
        => animal is not null ? this.mod.GetOverflowAnimalHearts(animal) : 0;

    public BigInteger GetAnimalOverflowFriendshipPoints(Character animal)
        => animal is not null ? this.mod.GetOverflowAnimalPoints(animal) : 0;

    public void ClearAnimalOverflowFriendship(Character animal) {
        if (animal is not null) this.mod.ClearAnimalOverflow(animal);
    }
}

sealed class Token(Func<Farmer, string, BigInteger> getHearts) {
    SortedDictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase);

    public bool AllowsInput() => true;

    public bool CanHaveMultipleValues(string? input = null) => string.IsNullOrWhiteSpace(input);

    public IEnumerable<string> GetValidInputs() => this.values.Keys;

    public bool HasBoundedRangeValues(string? input, out int min, out int max) {
        min = int.MinValue;
        max = int.MaxValue;
        return !string.IsNullOrWhiteSpace(input);
    }

    public bool TryValidateInput(string? input, out string? error) {
        if (string.IsNullOrWhiteSpace(input) || this.values.ContainsKey(input)) {
            error = null;
            return true;
        } else {
            error = $"invalid social NPC name: {input}";
            return false;
        }
    }

    public bool TryValidateValues(string? input, IEnumerable<string> values, out string? error) {
        if (!this.TryValidateInput(input, out error)) return false;

        var invalid = values
            .Where(string.IsNullOrWhiteSpace(input)
                ? v => !v.Contains(':')
                    || !int.TryParse(v[(v.LastIndexOf(':') + 1)..].Trim(), out _)
                : v => !int.TryParse(v.Trim(), out _)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalid.Any()) {
            var s = invalid.Length == 1 ? "" : "s";
            error = string.IsNullOrWhiteSpace(input)
                ? $"invalid value{s}: {string.Join(", ", invalid)}"
                : $"invalid integer{s}: {string.Join(", ", invalid)}";
            return false;
        }

        error = null;
        return true;
    }

    // FIXME: blocked on <https://github.com/Pathoschild/StardewMods/issues/1207>
    /*
    public string NormalizeValue(string value) {
        var i = value.LastIndexOf(':') + 1;
        return int.TryParse(value[i..].Trim(), out var n) ? value[..i] + n : value;
    }
    */

    public bool UpdateContext() {
        var oldValues = this.values;
        this.values = new(StringComparer.OrdinalIgnoreCase);

        var player = Game1.player ?? SaveGame.loaded?.player;
        if (player is not null) {
            foreach (var name in player.friendshipData?.Keys ?? []) this.values[name] = 0;

            var characters = Game1.characterData;
            if (characters is not null) foreach ((var name, var data) in characters) {
                if (data is not null && GameStateQuery.CheckConditions(data.CanSocialize)) {
                    this.values[name] = 0;
                }
            }

            if (Context.IsWorldReady) Utility.ForEachCharacter(npc => {
                if (Mod.NpcIsValid(npc)) this.values[npc.Name] = 0;
                return true;
            });

            foreach (var name in this.values.Keys.ToArray()) {
                this.values[name] = Utils.ToIntSaturating(getHearts(player, name));
            }
        }

        return !this.values.SequenceEqual(oldValues);
    }

    public bool IsReady() => this.values.Any();

    public IEnumerable<string> GetValues(string? input) => string.IsNullOrWhiteSpace(input)
        ? this.values.Select(p => $"{p.Key}:{p.Value}")
        : this.values.TryGetValue(input, out var hearts) ? [hearts.ToString()] : [];
}
