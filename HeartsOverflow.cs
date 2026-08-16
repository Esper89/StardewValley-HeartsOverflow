using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using StardewModdingAPI;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Delegates;
using StardewValley.GameData.Characters;
using StardewValley.Menus;
using StardewValley.Mods;
using StardewValley.Triggers;
using Netcode;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

sealed class Mod : StardewModdingAPI.Mod {
    public override void Entry(IModHelper helper) {
        Mod.instance = this;
        this.Config = helper.ReadConfig<Config>();
        this.font = Texture2D.FromStream(Game1.graphics.GraphicsDevice, Mod.Asset("font.png"));

        helper.Events.GameLoop.GameLaunched += (_, _) => this.onGameLaunched();
        Patches.Apply(this);
    }

    public override object GetApi() => new Api(this);

    static Mod? instance;
    internal static Mod Instance => Mod.instance ?? throw new NullReferenceException(
        $"Tried to access {typeof(Mod)} before initialization"
    );

    internal Config Config { get; private set; } = new();
    Texture2D? font;

    void onGameLaunched() {
        GameStateQuery.Register(
            $"{this.ModManifest.UniqueID}_PlayerOverflowFriendshipPoints",
            this.playerOverflowFriendshipPointsGameStateQuery
        );
        GameStateQuery.Register(
            $"{this.ModManifest.UniqueID}_PlayerTotalFriendshipPoints",
            this.playerTotalFriendshipPointsGameStateQuery
        );
        GameStateQuery.Register(
            $"{this.ModManifest.UniqueID}_PlayerOverflowHearts",
            this.playerOverflowHeartsGameStateQuery
        );
        GameStateQuery.Register(
            $"{this.ModManifest.UniqueID}_PlayerTotalHearts",
            this.playerTotalHeartsGameStateQuery
        );

        Event.RegisterPrecondition(
            $"{this.ModManifest.UniqueID}_OverflowFriendship",
            this.overflowFriendhsipEventPrecondition
        );
        Event.RegisterPrecondition(
            $"{this.ModManifest.UniqueID}_TotalFriendship",
            this.totalFriendhsipEventPrecondition
        );

        TriggerActionManager.RegisterAction(
            $"{this.ModManifest.UniqueID}_ClearOverflowFriendship",
            this.clearOverflowFriendshipTriggerAction
        );

        this.withApi<ContentPatcher.IContentPatcherAPI>("Pathoschild.ContentPatcher", cp => {
            cp.RegisterToken(this.ModManifest, "OverflowHearts", this.overflowHeartsToken());
            cp.RegisterToken(this.ModManifest, "TotalHearts", this.totalHeartsToken());
        });

        this.withApi<
            GenericModConfigMenu.IGenericModConfigMenuApi
        >("spacechase0.GenericModConfigMenu", gmcm => {
            gmcm.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new Config(),
                save: () => this.Helper.WriteConfig(this.Config)
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.NpcOverflowHearts,
                setValue: value => this.Config.NpcOverflowHearts = value,
                name: () => this.Helper.Translation.Get("config.npc-overflow-hearts.name"),
                tooltip: () => this.Helper.Translation.Get("config.npc-overflow-hearts.desc")
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.AnimalOverflowHearts,
                setValue: value => this.Config.AnimalOverflowHearts = value,
                name: () => this.Helper.Translation.Get("config.animal-overflow-hearts.name"),
                tooltip: () => this.Helper.Translation.Get("config.animal-overflow-hearts.desc")
            );

            this.withApi<GMCMOptions.IGMCMOptionsAPI>("jltaylor-us.GMCMOptions", gmcmOpts => {
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    getValue: () => this.Config.TextColorOverride is not null,
                    setValue: value => this.Config.TextColorOverride = value
                        ? new(Game1.textColor)
                        : null,
                    name: () => this.Helper.Translation.Get("config.override-text-color.name"),
                    tooltip: () => this.Helper.Translation.Get("config.override-text-color.desc")
                );
                gmcmOpts.AddColorOption(
                    mod: this.ModManifest,
                    getValue: () => this.Config.TextColorOverride?.AsColor() ?? Game1.textColor,
                    setValue: value => this.Config.TextColorOverride?.SetColor(value),
                    name: () => this.Helper.Translation.Get("config.text-color-override.name"),
                    tooltip: () => this.Helper.Translation.Get("config.text-color-override.desc")
                );
            });
        });
    }

    void withApi<T>(string id, Action<T> action) where T : class {
        try {
            var api = this.Helper.ModRegistry.GetApi<T>(id);
            if (api is not null) action(api);
        } catch (Exception e) {
            this.Monitor.Log($"Error calling mod API {id}: {e}", LogLevel.Error);
        }
    }

    static Stream? Asset(string assetFile) => typeof(Mod).Assembly
        .GetManifestResourceStream($"{nameof(HeartsOverflow)}.assets.{assetFile}");

    string npcModDataKey(string npcName)
        => $"{this.ModManifest.UniqueID}_OverflowFriendshipPoints[{npcName}]";

    string animalModDataKey()
        => $"{this.ModManifest.UniqueID}_OverflowFriendshipTowardFarmer";

    static BigInteger parsePoints(ModDataDictionary modData, string key)
        => modData.TryGetValue(key, out string data)
            ? BigInteger.TryParse(data, out BigInteger points) ? points : BigInteger.Zero
            : 0;

    internal BigInteger GetNpcPoints(Farmer player, NPC npc)
        => this.npcIsAllowed(npc)
            ? Mod.parsePoints(player.modData, this.npcModDataKey(npc.Name))
            : 0;

    internal BigInteger GetAnimalPoints(Character animal)
        => this.animalIsAllowed(animal)
            ? Mod.parsePoints(animal.modData, this.animalModDataKey())
            : 0;

    internal void ClearNpcPoints(Farmer player, NPC npc)
        => player.modData.Remove(this.npcModDataKey(npc.Name));

    internal void ClearAnimalPoints(Character animal)
        => animal.modData.Remove(this.animalModDataKey());

    internal void AddNpcPoints(Farmer player, NPC npc, int points) {
        if (!this.npcIsAllowed(npc)) return;

        var posessive = player.Name.EndsWith('s') ? "'" : "'s";
        var s = points == 1 ? "" : "s";
        this.Monitor.Log(
            $"{player.Name}{posessive} friendship with {npc.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = this.npcModDataKey(npc.Name);
        player.modData[key] = (Mod.parsePoints(player.modData, key) + points).ToString();
    }

    internal void AddAnimalPoints(Character animal, int points) {
        if (!this.animalIsAllowed(animal)) return;

        var s = points == 1 ? "" : "s";
        this.Monitor.Log(
            $"Friendship with {animal.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = this.animalModDataKey();
        animal.modData[key] = (Mod.parsePoints(animal.modData, key) + points).ToString();
    }

    internal BigInteger GetNpcHearts(Farmer player, NPC npc)
        => Mod.npcPointsToHearts(this.GetNpcPoints(player, npc));

    internal BigInteger GetAnimalHearts(Character animal)
        => Mod.animalPointsToHearts(this.GetAnimalPoints(animal));

    static BigInteger npcPointsToHearts(BigInteger points)
        => Utils.DivFloor(points, NPC.friendshipPointsPerHeartLevel);

    static BigInteger animalPointsToHearts(BigInteger points)
        => Utils.DivFloor(points, 200);

    bool npcIsAllowed(NPC npc)
        => this.Config.NpcOverflowHearts && (npc.CanSocialize || npc is Child);

    bool animalIsAllowed(Character animal)
        => this.Config.AnimalOverflowHearts && animal is Pet or FarmAnimal;

    BigInteger getNpcPointsByName(Farmer player, string npcName)
        => this.Config.NpcOverflowHearts ?
            Mod.parsePoints(player.modData, this.npcModDataKey(npcName))
            : 0;

    BigInteger getNpcHeartsByName(Farmer player, string npcName)
        => Mod.npcPointsToHearts(this.getNpcPointsByName(player, npcName));

    void clearNpcPointsByName(Farmer player, string npcName)
        => player.modData.Remove(this.npcModDataKey(npcName));

    internal void DrawHearts(SpriteBatch b, BigInteger hearts, int width, Vector2 at) {
        var text = $"{hearts:+#;-#;0}×";
        if (text.Length > width) {
            var sign = hearts > 0 ? "+" : hearts < 0 ? "-" : "";

            if (LocalizedContentManager.CurrentLanguageCode ==
                LocalizedContentManager.LanguageCode.en
            ) {
                var n = Math.Max(1, width - (3 + sign.Length));
                text = $"{sign}M{new('A', n)}X×";
            } else {
                var n = Math.Max(1, width - (1 + sign.Length));
                text = $"{sign}{new('▒', n)}×";
            }

            if (text.Length > width) return;
        }

        b.Draw(
            texture: Game1.mouseCursors,
            position: at - new Vector2(28, 0),
            sourceRectangle: new(hearts == 0 ? 218 : 211, 428, 7, 6),
            color: hearts < 0 ? Color.DarkGray : Color.White,
            rotation: 0,
            origin: Vector2.Zero,
            scale: 4,
            effects: SpriteEffects.None,
            layerDepth: 0.88f
        );

        foreach (var (c, i) in text.Reverse().Select((c, i) => (c, i))) {
            var glyph = c switch {
                '0' => 10,
                > '0' and <= '9' => c - '0',
                '+' => 11, '-' => 12, '×' => 13,
                'M' => 14, 'A' => 15, 'X' => 16, '▒' => 17,
                _ => 0,
            };

            b.Draw(
                texture: this.font,
                position: at - new Vector2(41 + i * 12, -3),
                sourceRectangle: new(glyph * 3, 0, 3, 5),
                color: this.Config.TextColorOverride?.AsColor() ?? Game1.textColor,
                rotation: 0,
                origin: Vector2.Zero,
                scale: 3,
                effects: SpriteEffects.None,
                layerDepth: 0.88f
            );
        }
    }

    bool playerOverflowFriendshipPointsGameStateQuery(
        string?[]? query, GameStateQueryContext context
    ) => Mod.gameStateQueryImpl(
        query, context,
        quantityName: "Points",
        getQuantity: this.getNpcPointsByName
    );

    bool playerTotalFriendshipPointsGameStateQuery(
        string?[]? query, GameStateQueryContext context
    ) => Mod.gameStateQueryImpl(
        query, context,
        quantityName: "Points",
        getQuantity: (player, npcName) => this.getNpcPointsByName(player, npcName) +
            player.getFriendshipLevelForNPC(npcName)
    );

    bool playerOverflowHeartsGameStateQuery(
        string?[]? query, GameStateQueryContext context
    ) => Mod.gameStateQueryImpl(
        query, context,
        quantityName: "Hearts",
        getQuantity: this.getNpcHeartsByName
    );

    bool playerTotalHeartsGameStateQuery(
        string?[]? query, GameStateQueryContext context
    ) => Mod.gameStateQueryImpl(
        query, context,
        quantityName: "Hearts",
        getQuantity: (player, npcName) => this.getNpcHeartsByName(player, npcName) +
            player.getFriendshipHeartLevelForNPC(npcName)
    );

    static bool gameStateQueryImpl(
        string?[]? query, GameStateQueryContext context,
        string quantityName, Func<Farmer, string, BigInteger> getQuantity
    ) {
        var args = Mod.gameStateQueryGetArgs(query, quantityName, out var error);
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
                        if (npc.CanSocialize || npc is Child) {
                            if (check(getQuantity(player, npc.Name))) hit = true;
                        }

                        return !hit;
                    });
                    return hit;
                } else if (anyDateableNpc) {
                    var hit = false;
                    Utility.ForEachCharacter(npc => {
                        if (npc.CanSocialize && npc.datable.Value) {
                            if (check(getQuantity(player, npc.Name))) hit = true;
                        }
                        return !hit;
                    });
                    return hit;
                } else {
                    return check(getQuantity(player, npcName));
                }
            });
        } else {
            return GameStateQuery.Helpers.ErrorResult(query, error);
        }
    }

    static (string, string, BigInteger, BigInteger?)? gameStateQueryGetArgs(
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

    bool overflowFriendhsipEventPrecondition(
        GameLocation? location, string? eventId, string?[]? args
    ) => Mod.eventPreconditionImpl(
        location, eventId, args,
        getPoints: this.getNpcPointsByName
    );

    bool totalFriendhsipEventPrecondition(
        GameLocation? location, string? eventId, string?[]? args
    ) => Mod.eventPreconditionImpl(
        location, eventId, args,
        getPoints: (player, npcName) => this.getNpcPointsByName(player, npcName) +
            player.getFriendshipLevelForNPC(npcName)
    );

    static bool eventPreconditionImpl(
        GameLocation? location, string? eventId, string?[]? args,
        Func<Farmer, string, BigInteger> getPoints
    ) {
        if (Game1.player is null) return false;

        var errorOut = new Utils.Box<string?>(null);
        var success = true;
        foreach (var (npcName, minPoints) in Mod.eventPreconditionGetArgs(args, errorOut)) {
            if (!success) continue;
            if (getPoints(Game1.player, npcName) < minPoints) success = false;
        }

        if (errorOut.Value is string error) {
            return Event.LogPreconditionError(location, eventId, args, error);
        } else return success;
    }

    static IEnumerable<(string, BigInteger)> eventPreconditionGetArgs(
        string?[]? args,
        Utils.Box<string?> errorOut
    ) {
        if (args is null) { errorOut.Value = "precondition args are null"; yield break; }
        if (args.Length > 0 && (args.Length - 1) % 2 != 0) {
            errorOut.Value = "precondition expects an even number of arguments"; yield break;
        }

        for (var i = 1; i < args.Length; i += 2) {
            var npcName = args[i];
            var minPointsString = args[i + 1];

            if (string.IsNullOrWhiteSpace(npcName)) {
                errorOut.Value = "first argument to precondition (npcName) is empty";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(minPointsString)) {
                errorOut.Value = "second argument to precondition (minPoints) is empty";
                yield break;
            }

            if (!BigInteger.TryParse(minPointsString, out var minPoints)) {
                errorOut.Value = "second argument to precondition (minPoints) has value " +
                    $"'{minPointsString}' which is not a valid integer";
                yield break;
            }

            yield return (npcName, minPoints);
        }
    }

    bool clearOverflowFriendshipTriggerAction(
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

        if (Game1.player is not null) this.clearNpcPointsByName(Game1.player, npcName);
        return true;
    }

    Token overflowHeartsToken() => new(this.getNpcHeartsByName);

    Token totalHeartsToken() => new((player, npcName)
        => this.getNpcHeartsByName(player, npcName) + player.getFriendshipHeartLevelForNPC(npcName)
    );
}

public sealed class Api : IHeartsOverflowApi {
    internal Api(Mod mod) {
        this.mod = mod;
    }

    Mod mod;

    public BigInteger GetNpcOverflowHearts(Farmer player, NPC npc)
        => npc is not null && player is not null ? this.mod.GetNpcHearts(player, npc) : 0;

    public BigInteger GetNpcOverflowFriendshipPoints(Farmer player, NPC npc)
        => npc is not null && player is not null ? this.mod.GetNpcPoints(player, npc) : 0;

    public void ClearNpcOverflowFriendship(Farmer player, NPC npc) {
        if (npc is not null && player is not null) this.mod.ClearNpcPoints(player, npc);
    }

    public BigInteger GetAnimalOverflowHearts(Character animal)
        => animal is not null ? this.mod.GetAnimalHearts(animal) : 0;

    public BigInteger GetAnimalOverflowFriendshipPoints(Character animal)
        => animal is not null ? this.mod.GetAnimalPoints(animal) : 0;

    public void ClearAnimalOverflowFriendship(Character animal) {
        if (animal is not null) this.mod.ClearAnimalPoints(animal);
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
                if (npc.CanSocialize || npc is Child) this.values[npc.Name] = 0;
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

sealed class Config {
    public bool NpcOverflowHearts { get; set; } = true;

    public bool AnimalOverflowHearts { get; set; } = true;

    public TextColor? TextColorOverride { get; set; } = null;

    internal sealed class TextColor {
        public TextColor(byte r, byte g, byte b, byte a) {
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        internal Color AsColor() => new(this.R, this.G, this.B, this.A);

        internal TextColor(Color color) => this.SetColor(color);

        internal void SetColor(Color color) {
            this.R = color.R;
            this.G = color.G;
            this.B = color.B;
            this.A = color.A;
        }
    }
}

static class Patches {
    internal static void Apply(Mod mod) {
        var patcher = new Patcher(new(mod.ModManifest.UniqueID), mod.Monitor);

        patcher.PatchMethod(
            typeof(Farmer), nameof(Farmer.changeFriendship),
            transpiler: nameof(Patches.transpile_Farmer_changeFriendship)
        );
        patcher.PatchMethod(
            typeof(FarmAnimal), "initNetFields",
            postfix: nameof(Patches.postfix_FarmAnimal_initNetFields)
        );
        patcher.PatchMethod(
            typeof(Pet), "initNetFields",
            postfix: nameof(Patches.postfix_Pet_initNetFields)
        );
        patcher.PatchMethod(
            typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Get),
            postfix: nameof(Patches.postfix_NetFieldBase_int_NetInt_Get)
        );
        patcher.PatchGetter(
            typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Value),
            postfix: nameof(Patches.postfix_NetFieldBase_int_NetInt_Value_get)
        );
        patcher.PatchMethod(
            typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)],
            postfix: nameof(Patches.postfix_Math_Min_int_int)
        );
        patcher.PatchMethod(
            typeof(NetInt), nameof(NetInt.Set),
            prefix: nameof(Patches.prefix_NetInt_Set)
        );
        patcher.PatchConstructor(
            typeof(SocialPage.SocialEntry),
            [typeof(NPC), typeof(Friendship), typeof(CharacterData), typeof(string)],
            postfix: nameof(Patches.postfix_SocialEntry_new)
        );
        patcher.PatchMethod(
            typeof(SocialPage), nameof(SocialPage.drawNPCSlot),
            postfix: nameof(Patches.postfix_SocialPage_drawNPCSlot)
        );
        patcher.PatchMethod(
            typeof(ProfileMenu), "drawNPCSlotHeart",
            prefix: nameof(Patches.prefix_ProfileMenu_drawNPCSlotHeart),
            postfix: nameof(Patches.postfix_ProfileMenu_drawNPCSlotHeart)
        );
        patcher.PatchConstructor(
            typeof(AnimalPage.AnimalEntry), [typeof(Character)],
            postfix: nameof(Patches.postfix_AnimalEntry_new)
        );
        patcher.PatchMethod(
            typeof(AnimalPage), "drawNPCSlot",
            transpiler: nameof(Patches.transpile_AnimalPage_drawNPCSlot),
            postfix: nameof(Patches.postfix_AnimalPage_drawNPCSlot)
        );
        patcher.PatchConstructor(
            typeof(AnimalQueryMenu), [typeof(FarmAnimal)],
            prefix: nameof(Patches.prefix_AnimalQueryMenu_new),
            transpiler: nameof(Patches.transpile_AnimalQueryMenu_new)
        );
        patcher.PatchMethod(
            typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.draw),
            transpiler: nameof(Patches.transpile_AnimalQueryMenu_draw),
            postfix: nameof(Patches.postfix_AnimalQueryMenu_draw)
        );
        patcher.PatchMethod(
            typeof(SocialPage), nameof(SocialPage.FindSocialCharacters),
            postfix: nameof(Patches.postfix_SocialPage_FindSocialCharacters)
        );
        patcher.PatchMethod(
            typeof(AnimalPage), nameof(AnimalPage.FindAnimals),
            postfix: nameof(Patches.postfix_AnimalPage_FindAnimals)
        );
    }

    static IEnumerable<CodeInstruction> transpile_Farmer_changeFriendship(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Call, AccessTools.DeclaredMethod(
                typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)]
            )),
        ])
        .ThrowIfNotMatch(
            $"Could not transpile method: Does not call {typeof(Math)}.{nameof(Math.Min)}" +
            $"({typeof(int)}, {typeof(int)})"
        )
        .InsertAndAdvance([
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Ldarg_2),
        ])
        .SetOperandAndAdvance(AccessTools.Method(
            typeof(Patches), nameof(Patches.patch_Farmer_changeFriendship_Math_Min)
        ))
        .InstructionEnumeration();

    static int patch_Farmer_changeFriendship_Math_Min(
        int total, int max,
        Farmer player, int amount, NPC npc
    ) {
        var overflow = total - max;
        if (overflow > 0 && amount > 0) Mod.Instance.AddNpcPoints(player, npc, overflow);
        return Math.Min(total, max);
    }

    static ConditionalWeakTable<NetInt, Character> animalFriendshipTowardFarmerField = new();

    static void postfix_FarmAnimal_initNetFields(FarmAnimal __instance)
        => Patches.animalFriendshipTowardFarmerField
            .Add(__instance.friendshipTowardFarmer, __instance);

    static void postfix_Pet_initNetFields(Pet __instance)
        => Patches.animalFriendshipTowardFarmerField
            .Add(__instance.friendshipTowardFarmer, __instance);

    static ThreadLocal<NetInt?> lastFriendshipTowardFarmerFieldAccessed = new(() => null);
    static ThreadLocal<int?> lastFriendshipTowardFarmerMinWith1000 = new(() => null);

    static void postfix_NetFieldBase_int_NetInt_Get(NetFieldBase<int, NetInt> __instance) {
        if (
            __instance is NetInt netInt &&
            Patches.animalFriendshipTowardFarmerField.TryGetValue(netInt, out _)
        ) {
            Patches.lastFriendshipTowardFarmerFieldAccessed.Value = netInt;
            Patches.lastFriendshipTowardFarmerMinWith1000.Value = null;
        }
    }

    static void postfix_NetFieldBase_int_NetInt_Value_get(NetFieldBase<int, NetInt> __instance)
        => Patches.postfix_NetFieldBase_int_NetInt_Get(__instance);

    static void postfix_Math_Min_int_int(int val1, int val2) {
        if (Patches.lastFriendshipTowardFarmerFieldAccessed.Value is not null) {
            if (val1 == 1000) Patches.lastFriendshipTowardFarmerMinWith1000.Value = val2;
            else if (val2 == 1000) Patches.lastFriendshipTowardFarmerMinWith1000.Value = val1;
        }
    }

    static void prefix_NetInt_Set(NetInt __instance, int newValue) {
        if (NetInt.ReferenceEquals(
            __instance,
            Patches.lastFriendshipTowardFarmerFieldAccessed.Value
        )) {
            Patches.lastFriendshipTowardFarmerFieldAccessed.Value = null;
            if (Patches.lastFriendshipTowardFarmerMinWith1000.Value is int valueBeforeMin) {
                Patches.lastFriendshipTowardFarmerMinWith1000.Value = null;
                if (Patches.animalFriendshipTowardFarmerField
                    .TryGetValue(__instance, out var animal)
                ) {
                    var oldValue = AccessTools.FieldRefAccess<NetInt, int>(__instance, "value");

                    if (newValue >= oldValue && valueBeforeMin > 1000 && newValue == 1000) {
                        Mod.Instance.AddAnimalPoints(animal, valueBeforeMin - 1000);
                    }
                }
            }
        }
    }

    static ConditionalWeakTable<SocialPage.SocialEntry, Utils.Box<BigInteger>>
        socialEntryOverflowHearts = new();

    static void postfix_SocialEntry_new(SocialPage.SocialEntry __instance) {
        if (__instance.Character is NPC npc) {
            var hearts = Mod.Instance.GetNpcHearts(Game1.player, npc);
            if (hearts != 0) Patches.socialEntryOverflowHearts.Add(__instance, new(hearts));
        }
    }

    static void postfix_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i) {
        var entry = __instance.GetSocialEntry(i);
        var hearts = Patches.socialEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
        if (hearts != 0) Mod.Instance.DrawHearts(b, hearts, 24, new(
            __instance.xPositionOnScreen + 632,
            __instance.sprites[i].bounds.Y + 8
        ));
    }

    static void prefix_ProfileMenu_drawNPCSlotHeart(
        ref float heartDrawStartY,
        SocialPage.SocialEntry entry
    ) {
        var hearts = Patches.socialEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
        if (hearts != 0 && Utility.GetMaximumHeartsForCharacter(entry.Character) <= 10) {
            heartDrawStartY -= 16;
        }
    }

    static void postfix_ProfileMenu_drawNPCSlotHeart(
        ProfileMenu __instance, SpriteBatch b,
        float heartDrawStartX, float heartDrawStartY,
        SocialPage.SocialEntry entry,
        int hearts
    ) {
        if (hearts != 0) return;
        var overflowHearts = Patches.socialEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
        if (overflowHearts != 0) {
            var heartDisplayPosition = AccessTools.FieldRefAccess<ProfileMenu, Vector2>(
                __instance, "_heartDisplayPosition"
            );

            var width = Utility.GetMaximumHeartsForCharacter(entry.Character) switch {
                <= 10 => 26,
                11 => 21, 12 => 18, 13 => 15, 14 => 13, 15 => 10, 16 => 7, 17 => 5, 18 => 2,
                > 18 => 0,
            };

            Mod.Instance.DrawHearts(b, overflowHearts, width, new(
                heartDrawStartX + 316,
                heartDisplayPosition.Y + heartDrawStartY + 32
            ));
        }
    }

    static ConditionalWeakTable<AnimalPage.AnimalEntry, Utils.Box<BigInteger>>
        animalEntryOverflowHearts = new();

    static void postfix_AnimalEntry_new(AnimalPage.AnimalEntry __instance) {
        var hearts = Mod.Instance.GetAnimalHearts(__instance.Animal);
        if (hearts != 0) Patches.animalEntryOverflowHearts.Add(__instance, new(hearts));
    }

    static IEnumerable<CodeInstruction> transpile_AnimalPage_drawNPCSlot(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Ldfld, AccessTools.DeclaredField(
                typeof(AnimalPage.AnimalEntry), nameof(AnimalPage.AnimalEntry.ReceivedAnimalCracker)
            )),
        ])
        .Repeat(matcher => matcher
            .InsertAndAdvance([
                new(OpCodes.Dup),
            ])
            .Advance(1)
            .Insert([
                new(OpCodes.Call, AccessTools.Method(
                    typeof(Patches),
                    nameof(Patches.patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker)
                )),
            ])
        )
        .InstructionEnumeration();

    static bool patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker(
        AnimalPage.AnimalEntry entry,
        bool value
    ) => value && Patches.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value == 0;

    static void postfix_AnimalPage_drawNPCSlot(AnimalPage __instance, SpriteBatch b, int i) {
        var entry = __instance.GetSocialEntry(i);
        var hearts = Patches.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
        if (hearts != 0) {
            var heightOffset = entry.TextureSourceRect.Height <= 16 ? -40 : 8;

            if (entry.ReceivedAnimalCracker) Utility.drawWithShadow(b,
                texture: Game1.objectSpriteSheet_2,
                position: new(
                    __instance.xPositionOnScreen + 564,
                    __instance.sprites[i].bounds.Y + heightOffset + 66
                ),
                sourceRect: new(16, 242, 15, 11),
                color: Color.White,
                rotation: 0,
                origin: Vector2.Zero,
                scale: 3,
                flipped: false,
                layerDepth: 0.8f,
                horizontalShadowOffset: -3,
                verticalShadowOffset: 3
            );

            Mod.Instance.DrawHearts(b, hearts, 11, new(
                __instance.xPositionOnScreen + 664,
                __instance.sprites[i].bounds.Y + heightOffset + 12
            ));
        }
    }

    static ConditionalWeakTable<AnimalQueryMenu, Utils.Box<BigInteger>>
        queryMenuOverflowHearts = new();

    static int? origAnimalQueryMenuHeight = null;

    static void prefix_AnimalQueryMenu_new(AnimalQueryMenu __instance, FarmAnimal animal) {
        Patches.origAnimalQueryMenuHeight ??= AnimalQueryMenu.height;
        AnimalQueryMenu.height = (int)Patches.origAnimalQueryMenuHeight;

        var hearts = Mod.Instance.GetAnimalHearts(animal);
        if (hearts != 0) {
            Patches.queryMenuOverflowHearts.Add(__instance, new(hearts));
            AnimalQueryMenu.height += 28;
        }
    }

    static IEnumerable<CodeInstruction> transpile_AnimalQueryMenu_new(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Stsfld, AccessTools.DeclaredField(
                typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.height)
            )),
        ])
        .Repeat(matcher => matcher
            .InsertAndAdvance([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, AccessTools.Method(
                    typeof(Patches), nameof(Patches.patch_AnimalQueryMenu_new_height)
                )),
            ])
            .Advance(1)
        )
        .InstructionEnumeration();

    static int patch_AnimalQueryMenu_new_height(int height, AnimalQueryMenu menu) => height + (
        Patches.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ? 28 : 0
    );

    static IEnumerable<CodeInstruction> transpile_AnimalQueryMenu_draw(
        IEnumerable<CodeInstruction> instructions
    ) {
        var matcher = new CodeMatcher(instructions);
        var ldloc = matcher
            .MatchEndForward([
                new(OpCodes.Ldfld, AccessTools.DeclaredField(
                    typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.parentName)
                )),
                new() { opcodes = [OpCodes.Brfalse, OpCodes.Brfalse_S] },
                new() { opcodes = Utils.OpCodeSets.Ldc_I4.ToList() },
                new() { opcodes = Utils.OpCodeSets.Stloc.ToList() },
            ])
            .ThrowIfNotMatch(
                $"Could not transpile method: Does not assert that {typeof(AnimalQueryMenu)}." +
                $"{nameof(AnimalQueryMenu.parentName)} is not null and then immediately assign " +
                $"a constant {typeof(int)} to a local variable"
            )
            .Instruction
            .StlocToLdloc();

        return matcher
            .Start()
            .MatchStartForward([
                new(ldloc),
            ])
            .Repeat(matcher => matcher
                .InsertAndAdvance([
                    new(OpCodes.Ldarg_0),
                ])
                .Advance(1)
                .Insert([
                    new(OpCodes.Call, AccessTools.Method(
                        typeof(Patches), nameof(Patches.patch_AnimalQueryMenu_draw_offset)
                    )),
                ])
            )
            .InstructionEnumeration();
    }

    static int patch_AnimalQueryMenu_draw_offset(AnimalQueryMenu menu, int value) => value + (
        Patches.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ? 28 : 0
    );

    static void postfix_AnimalQueryMenu_draw(AnimalQueryMenu __instance, SpriteBatch b) {
        var hearts = Patches.queryMenuOverflowHearts.GetValue(__instance, _ => new(0)).Value;
        if (hearts != 0) {
            var parentOffset = __instance.parentName is null ? 0 : 21;
            Mod.Instance.DrawHearts(b, hearts, 15, new(
                __instance.xPositionOnScreen + 252,
                __instance.yPositionOnScreen + parentOffset + 288
            ));
        }
    }

    static void postfix_SocialPage_FindSocialCharacters(List<SocialPage.SocialEntry> __result) {
        var mod = Mod.Instance;
        if (mod.Config.NpcOverflowHearts) {
            Utils.SortGroups<SocialPage.SocialEntry, int, BigInteger>(
                __result,
                entry => !entry.IsPlayer && !entry.IsChild && entry.Character is NPC
                    ? entry.Friendship?.Points ?? 0
                    : null,
                entry => entry.Character is NPC npc ? -mod.GetNpcPoints(Game1.player, npc) : 0
            );
        }
    }

    static void postfix_AnimalPage_FindAnimals(List<AnimalPage.AnimalEntry> __result) {
        if (Mod.Instance.Config.AnimalOverflowHearts) {
            Utils.SortGroups<AnimalPage.AnimalEntry, int, BigInteger>(
                __result,
                entry => entry.Animal is FarmAnimal a ? a.friendshipTowardFarmer.Value : null,
                entry => -Patches.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value
            );
        }
    }

    sealed class Patcher(Harmony harmony, IMonitor monitor) {
        internal void PatchMethod(
            Type type, string name, Type[]? parameters = null, Type[]? generics = null,
            string? prefix = null, string? transpiler = null, string? postfix = null
        ) {
            var pre = prefix is null ? null : new HarmonyMethod(typeof(Patches), prefix);
            var trans = transpiler is null ? null : new HarmonyMethod(typeof(Patches), transpiler);
            var post = postfix is null ? null : new HarmonyMethod(typeof(Patches), postfix);

            try {
                var original = AccessTools.DeclaredMethod(type, name, parameters, generics);
                harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
            } catch (Exception e) {
                var method = $"{type}.{name}";
                if (generics is not null) method +=
                    $"<{string.Join(",", generics.Select(g => g.ToString()))}>";
                if (parameters is not null) method +=
                    $"({string.Join(",", parameters.Select(p => p.ToString()))})";

                monitor.Log(
                    $"Error patching method {method}: {e}",
                    LogLevel.Error
                );
            }
        }

        internal void PatchGetter(
            Type type, string name,
            string? prefix = null, string? transpiler = null, string? postfix = null
        ) {
            var pre = prefix is null ? null : new HarmonyMethod(typeof(Patches), prefix);
            var trans = transpiler is null ? null : new HarmonyMethod(typeof(Patches), transpiler);
            var post = postfix is null ? null : new HarmonyMethod(typeof(Patches), postfix);

            try {
                var original = AccessTools.DeclaredPropertyGetter(type, name);
                harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
            } catch (Exception e) {
                monitor.Log(
                    $"Error patching property getter {type}.{name}: {e}",
                    LogLevel.Error
                );
            }
        }

        internal void PatchSetter(
            Type type, string name,
            string? prefix = null, string? transpiler = null, string? postfix = null
        ) {
            var pre = prefix is null ? null : new HarmonyMethod(typeof(Patches), prefix);
            var trans = transpiler is null ? null : new HarmonyMethod(typeof(Patches), transpiler);
            var post = postfix is null ? null : new HarmonyMethod(typeof(Patches), postfix);

            try {
                var original = AccessTools.DeclaredPropertySetter(type, name);
                harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
            } catch (Exception e) {
                monitor.Log(
                    $"Error patching property setter {type}.{name}: {e}",
                    LogLevel.Error
                );
            }
        }

        internal void PatchConstructor(
            Type type, Type[]? parameters = null,
            string? prefix = null, string? transpiler = null, string? postfix = null
        ) {
            var pre = prefix is null ? null : new HarmonyMethod(typeof(Patches), prefix);
            var trans = transpiler is null ? null : new HarmonyMethod(typeof(Patches), transpiler);
            var post = postfix is null ? null : new HarmonyMethod(typeof(Patches), postfix);

            try {
                var original = AccessTools.DeclaredConstructor(type, parameters);
                harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
            } catch (Exception e) {
                var constructor = $"{type}";
                if (parameters is not null) constructor +=
                    $"({string.Join(",", parameters.Select(p => p.ToString()))})";

                monitor.Log(
                    $"Error patching constructor {constructor}: {e}",
                    LogLevel.Error
                );
            }
        }
    }
}

static class Utils {
    internal static int ToIntSaturating(BigInteger n)
        => n > int.MaxValue ? int.MaxValue : n < int.MinValue ? int.MinValue : (int)n;

    internal static BigInteger DivFloor(BigInteger a, int b)
        => (((a < 0) ^ (b < 0)) && (a % b != 0)) ? a / b - 1 : a / b;

    internal static void SortGroups<T, G, K>(IList<T> list, Func<T, G?> group, Func<T, K> key)
    where G : struct, IEquatable<G> where K : IComparable<K> {
        int? sort = null;
        G? prev = null;
        for (int i = 0; i < list.Count; i++) {
            var curr = group(list[i]);

            if (curr is not null && curr.Equals(prev)) sort ??= i - 1;
            else if (sort is not null) {
                Utils.SortStable(list, (int)sort, i, key);
                sort = null;
            }

            prev = curr;
        }

        if (sort is not null) Utils.SortStable(list, (int)sort, list.Count, key);
    }

    internal static void SortStable<T, K>(IList<T> list, int start, int end, Func<T, K> key)
    where K : IComparable<K> {
        for (int i = start + 1; i < end; i++) {
            var v = list[i];
            var k = key(v);

            int j;
            for (j = i - 1; j >= start; j--) {
                if (k.CompareTo(key(list[j])) >= 0) break;
                list[j + 1] = list[j];
            }

            list[j + 1] = v;
        }
    }

    internal static class OpCodeSets {
        internal static readonly OpCode[] Stloc = [
            OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3, OpCodes.Stloc_S,
            OpCodes.Stloc,
        ];

        internal static readonly OpCode[] Ldc_I4 = [
            OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7,
            OpCodes.Ldc_I4_8, OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_S, OpCodes.Ldc_I4,
        ];
    }

    internal static CodeInstruction StlocToLdloc(this CodeInstruction instr)
        => instr.opcode switch {
            var op when op == OpCodes.Stloc_0 => new(OpCodes.Ldloc_0),
            var op when op == OpCodes.Stloc_1 => new(OpCodes.Ldloc_1),
            var op when op == OpCodes.Stloc_2 => new(OpCodes.Ldloc_2),
            var op when op == OpCodes.Stloc_3 => new(OpCodes.Ldloc_3),
            var op when op == OpCodes.Stloc_S => new(OpCodes.Ldloc_S, instr.operand),
            var op when op == OpCodes.Stloc => new(OpCodes.Ldloc, instr.operand),
            _ => throw new ArgumentException("invalid opcode", nameof(instr)),
        };

    internal class Box<T> {
        internal Box(T value) => this.Value = value;
        internal T Value;
    }
}
