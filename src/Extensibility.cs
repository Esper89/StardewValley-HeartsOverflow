using System.Numerics;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.Triggers;
using StardewModdingAPI;

namespace HeartsOverflow;

sealed class Extensibility(Mod mod) {
    internal void Register() {
        mod.WithApi<ContentPatcher.IContentPatcherAPI>("Pathoschild.ContentPatcher", cp => {
            cp.RegisterToken(mod.ModManifest, "TotalHearts", this.totalHeartsToken);
            cp.RegisterToken(mod.ModManifest, "OverflowHearts", this.overflowHeartsToken);
        });

        try {
            GameStateQuery.Register(
                $"{mod.ModManifest.UniqueID}_PlayerTotalHearts",
                this.playerTotalHeartsGsq
            );
            GameStateQuery.Register(
                $"{mod.ModManifest.UniqueID}_PlayerOverflowHearts",
                this.playerOverflowHeartsGsq
            );
            GameStateQuery.Register(
                $"{mod.ModManifest.UniqueID}_PlayerTotalFriendshipPoints",
                this.playerTotalFriendshipPointsGsq
            );
            GameStateQuery.Register(
                $"{mod.ModManifest.UniqueID}_PlayerOverflowFriendshipPoints",
                this.playerOverflowFriendshipPointsGsq
            );
        } catch (Exception e) {
            mod.Monitor.Log(
                $"error registering game state queries: exception {e}", LogLevel.Error
            );
        }

        try {
            Event.RegisterPrecondition(
                $"{mod.ModManifest.UniqueID}_TotalFriendship",
                this.totalFriendhsipPrecond
            );
            Event.RegisterPrecondition(
                $"{mod.ModManifest.UniqueID}_OverflowHearts",
                this.overflowHeartsPrecond
            );
        } catch (Exception e) {
            mod.Monitor.Log(
                $"error registering event preconditions: exception {e}", LogLevel.Error
            );
        }

        try {
            TriggerActionManager.RegisterAction(
                $"{mod.ModManifest.UniqueID}_ClearOverflowFriendship",
                this.clearOverflowFriendshipAction
            );
        } catch (Exception e) {
            mod.Monitor.Log(
                $"error registering trigger action: exception {e}", LogLevel.Error
            );
        }
    }

    object totalHeartsToken => new TokenImpl<TotalHeartsQuantity>(new(mod));

    object overflowHeartsToken => new TokenImpl<OverflowHeartsQuantity>(new(mod));

    sealed class TokenImpl<Q>(Q quantity) where Q : INpcQuantity {
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

        public bool TryValidateValues(
            string? input, IEnumerable<string> values, out string? error
        ) {
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
                    if (Hearts.NpcIsValid(npc)) this.values[npc.Name] = 0;
                    return true;
                });

                foreach (var name in this.values.Keys.ToArray()) {
                    this.values[name] = Utils.ToIntSaturating(quantity.Get(player, name));
                }
            }

            return !this.values.SequenceEqual(oldValues);
        }

        public bool IsReady() => this.values.Any();

        public IEnumerable<string> GetValues(string? input) => string.IsNullOrWhiteSpace(input)
            ? this.values.Select(p => $"{p.Key}:{p.Value}")
            : this.values.TryGetValue(input, out var hearts) ? [hearts.ToString()] : [];
    }

    bool playerTotalHeartsGsq(
        string?[]? query, GameStateQueryContext context
    ) => Extensibility.gsqImpl(query, context, "Hearts", new TotalHeartsQuantity(mod));

    bool playerOverflowHeartsGsq(
        string?[]? query, GameStateQueryContext context
    ) => Extensibility.gsqImpl(query, context, "Hearts", new OverflowHeartsQuantity(mod));

    bool playerTotalFriendshipPointsGsq(
        string?[]? query, GameStateQueryContext context
    ) => Extensibility.gsqImpl(query, context, "Points", new TotalPointsQuantity(mod));

    bool playerOverflowFriendshipPointsGsq(
        string?[]? query, GameStateQueryContext context
    ) => Extensibility.gsqImpl(query, context, "Points", new OverflowPointsQuantity(mod));

    static bool gsqImpl<Q>(
        string?[]? query, GameStateQueryContext context,
        string quantityName, Q quantity
    ) where Q : INpcQuantity {
        var args = Extensibility.gsqGetArgs(query, quantityName, out var error);
        if (args is (var playerKey, var npcName, var min, var max)) {
            bool check(BigInteger num)
                => num >= min && (max is null || num <= max);

            var anyNpc = string.Equals(
                npcName, "Any",
                StringComparison.OrdinalIgnoreCase
            );
            var anyDateableNpc = !anyNpc && string.Equals(
                npcName, "AnyDateable",
                StringComparison.OrdinalIgnoreCase
            );

            return GameStateQuery.Helpers.WithPlayer(context.Player, playerKey, player => {
                if (player is null) return false;

                if (anyNpc) {
                    var hit = false;
                    Utility.ForEachCharacter(npc => {
                        if (Hearts.NpcIsValid(npc)) {
                            if (check(quantity.Get(player, npc.Name))) hit = true;
                        }

                        return !hit;
                    });
                    return hit;
                } else if (anyDateableNpc) {
                    var hit = false;
                    Utility.ForEachCharacter(npc => {
                        if (Hearts.NpcIsValid(npc) && npc.datable.Value) {
                            if (check(quantity.Get(player, npc.Name))) hit = true;
                        }
                        return !hit;
                    });
                    return hit;
                } else {
                    return check(quantity.Get(player, npcName));
                }
            });
        } else {
            return GameStateQuery.Helpers.ErrorResult(query, error);
        }
    }

    static (string, string, BigInteger, BigInteger?)? gsqGetArgs(
        string?[]? query, string quantityName, out string error
    ) {
        error = "";

        if (query is null) { error = "query is null"; return null; }
        var args = query.Length > 0 ? query.Length - 1 : 0;
        if (args > 4) { error = $"query expected at most 4 arguments, found {args}"; return null; }
        if (args < 3) { error = $"query expected at least 3 arguments, found {args}"; return null; }

        var playerKey = query[1];
        var npcName = query[2];
        var minString = query[3];
        var maxString = args == 4 ? query[4] : null;

        if (string.IsNullOrWhiteSpace(playerKey)) {
            error = "first argument to query (playerKey) is empty";
            return null;
        }

        if (string.IsNullOrWhiteSpace(npcName)) {
            error = "second argument to query (npcName) is empty";
            return null;
        }

        if (string.IsNullOrWhiteSpace(minString)) {
            error = $"third argument to query (min{quantityName}) is empty";
            return null;
        }

        if (!BigInteger.TryParse(minString, out var minNum)) {
            error = $"third argument to query (min{quantityName}) has value '{minString}' which " +
                "is not a valid integer";
            return null;
        }

        if (string.IsNullOrWhiteSpace(maxString)) {
            return (playerKey, npcName, minNum, null);
        } else if (BigInteger.TryParse(maxString, out var maxNum)) {
            return (playerKey, npcName, minNum, maxNum);
        } else {
            error = $"fourth argument to query (max{quantityName}) has value '{maxString}' which " +
                "is not a valid integer";
            return null;
        }
    }

    bool totalFriendhsipPrecond(
        GameLocation? location, string? eventId, string?[]? args
    ) => Extensibility.precondImpl(
        location, eventId, args,
        "Points", new TotalPointsQuantity(mod)
    );

    bool overflowHeartsPrecond(
        GameLocation? location, string? eventId, string?[]? args
    ) => Extensibility.precondImpl(
        location, eventId, args, "Hearts", new OverflowHeartsQuantity(mod)
    );

    static bool precondImpl<Q>(
        GameLocation? location, string? eventId, string?[]? args,
        string quantityName, Q quantity
    ) where Q : INpcQuantity {
        if (Game1.player is null) return false;

        var errorOut = new Utils.Box<string?>(null);
        var parsedArgs = Extensibility.precondGetArgs(args, quantityName, errorOut);
        var success = true;
        foreach (var (npcName, min) in parsedArgs) {
            if (!success) continue;
            if (quantity.Get(Game1.player, npcName) < min) success = false;
        }

        if (errorOut.Value is string error) {
            return Event.LogPreconditionError(location, eventId, args, error);
        } else return success;
    }

    static IEnumerable<(string, BigInteger)> precondGetArgs(
        string?[]? args,
        string quantityName, Utils.Box<string?> errorOut
    ) {
        if (args is null) { errorOut.Value = "precondition args are null"; yield break; }
        if (args.Length > 0 && (args.Length - 1) % 2 != 0) {
            errorOut.Value = "precondition expects an even number of arguments"; yield break;
        }

        for (var i = 1; i < args.Length; i += 2) {
            var npcName = args[i];
            var minString = args[i + 1];

            if (string.IsNullOrWhiteSpace(npcName)) {
                errorOut.Value = "first argument to precondition (npcName) is empty";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(minString)) {
                errorOut.Value = $"second argument to precondition (min{quantityName}) is empty";
                yield break;
            }

            if (!BigInteger.TryParse(minString, out var minNum)) {
                errorOut.Value = $"second argument to precondition (min{quantityName}) has value " +
                    $"'{minString}' which is not a valid integer";
                yield break;
            }

            yield return (npcName, minNum);
        }
    }

    bool clearOverflowFriendshipAction(
        string?[]? args, TriggerActionContext context, out string error
    ) {
        error = "";

        if (args is null) { error = "action args are null"; return false; }
        if (args.Length < 2) {
            error = "action expects at least 1 argument, found 0";
            return false;
        }
        if (args.Length > 2) {
            error = "action expects at most 1 argument, found {args.Length - 1}";
            return false;
        }

        var npcName = args[1];

        if (string.IsNullOrWhiteSpace(npcName)) {
            error = "first argument to action (npcName) is empty";
            return false;
        }

        if (Game1.player is not null) {
            Hearts.NpcByName(mod, Game1.player, npcName).ClearOverflow();
        }
        return true;
    }

    interface INpcQuantity {
        BigInteger Get(Farmer player, string npcName);
    }

    readonly struct TotalHeartsQuantity(Mod mod) : INpcQuantity {
        BigInteger INpcQuantity.Get(Farmer player, string npcName)
            => Hearts.NpcByName(mod, player, npcName).TotalHearts;
    }

    readonly struct OverflowHeartsQuantity(Mod mod) : INpcQuantity {
        BigInteger INpcQuantity.Get(Farmer player, string npcName)
            => Hearts.NpcByName(mod, player, npcName).OverflowHearts;
    }

    readonly struct TotalPointsQuantity(Mod mod) : INpcQuantity {
        BigInteger INpcQuantity.Get(Farmer player, string npcName)
            => Hearts.NpcByName(mod, player, npcName).TotalPoints;
    }

    readonly struct OverflowPointsQuantity(Mod mod) : INpcQuantity {
        BigInteger INpcQuantity.Get(Farmer player, string npcName)
            => Hearts.NpcByName(mod, player, npcName).OverflowPoints;
    }
}
