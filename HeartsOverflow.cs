using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using StardewModdingAPI;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Mods;
using StardewValley.Characters;
using StardewValley.Menus;
using Netcode;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

sealed class Mod : StardewModdingAPI.Mod {
    public override void Entry(IModHelper helper) {
        Mod.instance = this;
        this.config = helper.ReadConfig<Config>();
        this.font = Texture2D.FromStream(Game1.graphics.GraphicsDevice, Mod.Asset("font.png"));

        helper.Events.GameLoop.GameLaunched += (_, _) => this.onGameLaunched();

        this.patchMethod(
            typeof(Farmer), nameof(Farmer.changeFriendship),
            transpiler: nameof(Mod.transpile_Farmer_changeFriendship)
        );
        this.patchMethod(
            typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Get),
            postfix: nameof(Mod.postfix_NetFieldBase_int_NetInt_Get)
        );
        this.patchGetter(
            typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Value),
            postfix: nameof(Mod.postfix_NetFieldBase_int_NetInt_Value_get)
        );
        this.patchMethod(
            typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)],
            postfix: nameof(Mod.postfix_Math_Min_int_int)
        );
        this.patchMethod(
            typeof(FarmAnimal), "initNetFields",
            postfix: nameof(Mod.postfix_FarmAnimal_initNetFields)
        );
        this.patchMethod(
            typeof(Pet), "initNetFields",
            postfix: nameof(Mod.postfix_Pet_initNetFields)
        );
        this.patchMethod(
            typeof(SocialPage), nameof(SocialPage.drawNPCSlot),
            postfix: nameof(Mod.postfix_SocialPage_drawNPCSlot)
        );
        this.patchMethod(
            typeof(ProfileMenu), "drawNPCSlotHeart",
            prefix: nameof(Mod.prefix_ProfileMenu_drawNPCSlotHeart),
            postfix: nameof(Mod.postfix_ProfileMenu_drawNPCSlotHeart)
        );
        this.patchConstructor(
            typeof(AnimalPage.AnimalEntry), [typeof(Character)],
            postfix: nameof(Mod.postfix_AnimalEntry_new)
        );
        this.patchMethod(
            typeof(AnimalPage), "drawNPCSlot",
            transpiler: nameof(Mod.transpile_AnimalPage_drawNPCSlot),
            postfix: nameof(Mod.postfix_AnimalPage_drawNPCSlot)
        );
        this.patchConstructor(
            typeof(AnimalQueryMenu), [typeof(FarmAnimal)],
            prefix: nameof(Mod.prefix_AnimalQueryMenu_new),
            transpiler: nameof(Mod.transpile_AnimalQueryMenu_new)
        );
        this.patchMethod(
            typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.draw),
            transpiler: nameof(Mod.transpile_AnimalQueryMenu_draw),
            postfix: nameof(Mod.postfix_AnimalQueryMenu_draw)
        );
        this.patchMethod(
            typeof(SocialPage), nameof(SocialPage.FindSocialCharacters),
            postfix: nameof(Mod.postfix_SocialPage_FindSocialCharacters)
        );
        this.patchMethod(
            typeof(AnimalPage), nameof(AnimalPage.FindAnimals),
            postfix: nameof(Mod.postfix_AnimalPage_FindAnimals)
        );
    }

    public override object GetApi() => new Api(this);

    void onGameLaunched() {
        this.withApi<
            GenericModConfigMenu.IGenericModConfigMenuApi
        >("spacechase0.GenericModConfigMenu", gmcm => {
            gmcm.Register(
                mod: this.ModManifest,
                reset: () => this.config = new Config(),
                save: () => this.Helper.WriteConfig(this.config)
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.config.ShowNpcHearts,
                setValue: value => this.config.ShowNpcHearts = value,
                name: () => this.Helper.Translation.Get("config.show-npc-hearts.name"),
                tooltip: () => this.Helper.Translation.Get("config.show-npc-hearts.desc")
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.config.ShowAnimalHearts,
                setValue: value => this.config.ShowAnimalHearts = value,
                name: () => this.Helper.Translation.Get("config.show-animal-hearts.name"),
                tooltip: () => this.Helper.Translation.Get("config.show-animal-hearts.desc")
            );

            this.withApi<GMCMOptions.IGMCMOptionsAPI>("jltaylor-us.GMCMOptions", gmcmOpts => {
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    getValue: () => this.config.TextColorOverride is not null,
                    setValue: value => this.config.TextColorOverride = value
                        ? new(Game1.textColor)
                        : null,
                    name: () => this.Helper.Translation.Get("config.override-text-color.name"),
                    tooltip: () => this.Helper.Translation.Get("config.override-text-color.desc")
                );
                gmcmOpts.AddColorOption(
                    mod: this.ModManifest,
                    getValue: () => this.config.TextColorOverride?.AsColor() ?? Game1.textColor,
                    setValue: value => this.config.TextColorOverride?.SetColor(value),
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

    static Mod? instance;
    internal static Mod Instance => Mod.instance ?? throw new NullReferenceException(
        $"Tried to access {typeof(Mod)} before initialization"
    );

    Harmony harmony = new Harmony("Esper89.HeartsOverflow");
    Config config = new();
    Texture2D? font;

    void patchMethod(
        Type type, string name, Type[]? parameters = null, Type[]? generics = null,
        string? prefix = null, string? transpiler = null, string? postfix = null
    ) {
        var pre = prefix is null ? null : new HarmonyMethod(typeof(Mod), prefix);
        var trans = transpiler is null ? null : new HarmonyMethod(typeof(Mod), transpiler);
        var post = postfix is null ? null : new HarmonyMethod(typeof(Mod), postfix);

        try {
            var original = AccessTools.DeclaredMethod(type, name, parameters, generics);
            this.harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
        } catch (Exception e) {
            var method = $"{type}.{name}";
            if (generics is not null) method +=
                $"<{string.Join(",", generics.Select(g => g.ToString()))}>";
            if (parameters is not null) method +=
                $"({string.Join(",", parameters.Select(p => p.ToString()))})";

            this.Monitor.Log($"Error patching method {method}: {e}", LogLevel.Error);
        }
    }

    void patchGetter(
        Type type, string name,
        string? prefix = null, string? transpiler = null, string? postfix = null
    ) {
        var pre = prefix is null ? null : new HarmonyMethod(typeof(Mod), prefix);
        var trans = transpiler is null ? null : new HarmonyMethod(typeof(Mod), transpiler);
        var post = postfix is null ? null : new HarmonyMethod(typeof(Mod), postfix);

        try {
            var original = AccessTools.DeclaredPropertyGetter(type, name);
            this.harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
        } catch (Exception e) {
            this.Monitor.Log($"Error patching property getter {type}.{name}: {e}", LogLevel.Error);
        }
    }

    void patchSetter(
        Type type, string name,
        string? prefix = null, string? transpiler = null, string? postfix = null
    ) {
        var pre = prefix is null ? null : new HarmonyMethod(typeof(Mod), prefix);
        var trans = transpiler is null ? null : new HarmonyMethod(typeof(Mod), transpiler);
        var post = postfix is null ? null : new HarmonyMethod(typeof(Mod), postfix);

        try {
            var original = AccessTools.DeclaredPropertySetter(type, name);
            this.harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
        } catch (Exception e) {
            this.Monitor.Log($"Error patching property setter {type}.{name}: {e}", LogLevel.Error);
        }
    }

    void patchConstructor(
        Type type, Type[]? parameters = null,
        string? prefix = null, string? transpiler = null, string? postfix = null
    ) {
        var pre = prefix is null ? null : new HarmonyMethod(typeof(Mod), prefix);
        var trans = transpiler is null ? null : new HarmonyMethod(typeof(Mod), transpiler);
        var post = postfix is null ? null : new HarmonyMethod(typeof(Mod), postfix);

        try {
            var original = AccessTools.DeclaredConstructor(type, parameters);
            this.harmony.Patch(original, prefix: pre, transpiler: trans, postfix: post);
        } catch (Exception e) {
            var constructor = $"{type}";
            if (parameters is not null) constructor +=
                $"({string.Join(",", parameters.Select(p => p.ToString()))})";

            this.Monitor.Log($"Error patching constructor {constructor}: {e}", LogLevel.Error);
        }
    }

    string npcModDataKey(NPC npc)
        => $"{this.ModManifest.UniqueID}_OverflowFriendshipPoints[{npc.Name}]";

    string animalModDataKey()
        => $"{this.ModManifest.UniqueID}_OverflowFriendshipTowardFarmer";

    static BigInteger parsePoints(ModDataDictionary modData, string key)
        => modData.TryGetValue(key, out string data)
            ? BigInteger.TryParse(data, out BigInteger points) ? points : BigInteger.Zero
            : 0;

    internal BigInteger GetNpcPoints(Farmer player, NPC npc)
        => Mod.npcIsValid(npc) ? Mod.parsePoints(player.modData, this.npcModDataKey(npc)) : 0;

    internal BigInteger GetAnimalPoints(Character animal)
        => Mod.animalIsValid(animal) ? Mod.parsePoints(animal.modData, this.animalModDataKey()) : 0;

    internal void ClearNpcPoints(Farmer player, NPC npc)
        => player.modData.Remove(this.npcModDataKey(npc));

    internal void ClearAnimalPoints(Character animal)
        => animal.modData.Remove(this.animalModDataKey());

    void addNpcPoints(Farmer player, NPC npc, int points) {
        if (!Mod.npcIsValid(npc)) return;

        var posessive = player.Name.EndsWith('s') ? "'" : "'s";
        var s = points == 1 ? "" : "s";
        this.Monitor.Log(
            $"{player.Name}{posessive} friendship with {npc.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = this.npcModDataKey(npc);
        player.modData[key] = (Mod.parsePoints(player.modData, key) + points).ToString();
    }

    void addAnimalPoints(Character animal, int points) {
        if (!Mod.animalIsValid(animal)) return;

        var s = points == 1 ? "" : "s";
        this.Monitor.Log(
            $"Friendship with {animal.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = this.animalModDataKey();
        animal.modData[key] = (Mod.parsePoints(animal.modData, key) + points).ToString();
    }

    internal BigInteger GetNpcHearts(Farmer player, NPC npc)
        => Mod.hearts(this.GetNpcPoints(player, npc));

    internal BigInteger GetAnimalHearts(Character animal)
        => Mod.hearts(this.GetAnimalPoints(animal));

    static BigInteger hearts(BigInteger points)
        => points > 0 ? points / NPC.friendshipPointsPerHeartLevel : 0;

    static bool npcIsValid(NPC npc) => npc.CanSocialize;

    static bool animalIsValid(Character animal) => animal is Pet or FarmAnimal;

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
            typeof(Mod), nameof(Mod.patch_Farmer_changeFriendship_Math_Min)
        ))
        .InstructionEnumeration();

    static int patch_Farmer_changeFriendship_Math_Min(
        int total, int max,
        Farmer player, int amount, NPC npc
    ) {
        var mod = Mod.Instance;

        var overflow = total - max;
        if (overflow > 0 && amount > 0) mod.addNpcPoints(player, npc, overflow);
        return Math.Min(total, max);
    }

    static ThreadLocal<NetInt?> lastNetIntAccessed = new(() => null);
    static ThreadLocal<int?> lastMinWith1000 = new(() => null);

    static void postfix_NetFieldBase_int_NetInt_Get(NetFieldBase<int, NetInt> __instance) {
        if (__instance is NetInt netInt) {
            Mod.lastNetIntAccessed.Value = netInt;
            Mod.lastMinWith1000.Value = null;
        }
    }

    static void postfix_NetFieldBase_int_NetInt_Value_get(NetFieldBase<int, NetInt> __instance)
        => Mod.postfix_NetFieldBase_int_NetInt_Get(__instance);

    static void postfix_Math_Min_int_int(int val1, int val2) {
        if (Mod.lastNetIntAccessed.Value is not null) {
            if (val1 == 1000) Mod.lastMinWith1000.Value = val2;
            else if (val2 == 1000) Mod.lastMinWith1000.Value = val1;
        }
    }

    static void postfix_FarmAnimal_initNetFields(FarmAnimal __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent += (netInt, oldValue, newValue)
            => Mod.Instance.animalFriendshipChanged(__instance, netInt, oldValue, newValue);

    static void postfix_Pet_initNetFields(Pet __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent += (netInt, oldValue, newValue)
            => Mod.Instance.animalFriendshipChanged(__instance, netInt, oldValue, newValue);

    void animalFriendshipChanged(
        Character animal, NetInt friendshipTowardFarmer,
        int oldValue, int newValue
    ) {
        if (
            NetInt.ReferenceEquals(Mod.lastNetIntAccessed.Value, friendshipTowardFarmer) &&
            Mod.lastMinWith1000.Value is int valueBeforeMin
        ) {
            Mod.lastNetIntAccessed.Value = null;
            Mod.lastMinWith1000.Value = null;
            if (newValue >= oldValue && valueBeforeMin > 1000 && newValue == 1000) {
                this.addAnimalPoints(animal, valueBeforeMin - 1000);
            }
        }
    }

    static void postfix_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i) {
        var mod = Mod.Instance;

        if (mod.config.ShowNpcHearts && __instance.GetSocialEntry(i).Character is NPC npc) {
            var hearts = mod.GetNpcHearts(Game1.player, npc);
            if (hearts != 0) mod.drawHearts(b, hearts, 24, new(
                __instance.xPositionOnScreen + 632,
                __instance.sprites[i].bounds.Y + 8
            ));
        }
    }

    static void prefix_ProfileMenu_drawNPCSlotHeart(
        ref float heartDrawStartY,
        SocialPage.SocialEntry entry
    ) {
        var mod = Mod.Instance;

        if (mod.config.ShowNpcHearts && entry.Character is NPC npc) {
            var overflowHearts = mod.GetNpcHearts(Game1.player, npc);
            if (overflowHearts != 0 && heartDrawStartY >= 0) heartDrawStartY -= 16;
        }
    }

    static void postfix_ProfileMenu_drawNPCSlotHeart(
        ProfileMenu __instance, SpriteBatch b,
        float heartDrawStartX, float heartDrawStartY,
        SocialPage.SocialEntry entry,
        int hearts
    ) {
        var mod = Mod.Instance;

        if (mod.config.ShowNpcHearts && entry.Character is NPC npc) {
            var overflowHearts = mod.GetNpcHearts(Game1.player, npc);
            if (hearts == 0 && overflowHearts != 0) {
                var heartDisplayPosition = AccessTools.FieldRefAccess<ProfileMenu, Vector2>(
                    __instance, "_heartDisplayPosition"
                );

                mod.drawHearts(b, overflowHearts, heartDrawStartY < 0 ? 13 : 26, new(
                    heartDrawStartX + 316,
                    heartDisplayPosition.Y + heartDrawStartY + 32
                ));
            }
        }
    }

    static ConditionalWeakTable<AnimalPage.AnimalEntry, Utils.Box<BigInteger>>
        animalEntryOverflowHearts = new();

    static void postfix_AnimalEntry_new(AnimalPage.AnimalEntry __instance)
        => Mod.animalEntryOverflowHearts
            .Add(__instance, new(Mod.Instance.GetAnimalHearts(__instance.Animal)));

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
                    typeof(Mod), nameof(Mod.patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker)
                )),
            ])
        )
        .InstructionEnumeration();

    static bool patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker(
        AnimalPage.AnimalEntry entry,
        bool value
    ) => value && !(
        Mod.Instance.config.ShowAnimalHearts &&
        Mod.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value != 0
    );

    static void postfix_AnimalPage_drawNPCSlot(AnimalPage __instance, SpriteBatch b, int i) {
        var mod = Mod.Instance;

        if (mod.config.ShowAnimalHearts) {
            var entry = __instance.GetSocialEntry(i);
            var hearts = Mod.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
            var heightOffset = entry.TextureSourceRect.Height <= 16 ? -40 : 8;

            if (hearts != 0) {
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
                    layerDepth: 0.8f
                );

                mod.drawHearts(b, hearts, 11, new(
                    __instance.xPositionOnScreen + 664,
                    __instance.sprites[i].bounds.Y + heightOffset + 12
                ));
            }
        }
    }

    static ConditionalWeakTable<AnimalQueryMenu, Utils.Box<BigInteger>>
        queryMenuOverflowHearts = new();

    static void prefix_AnimalQueryMenu_new(AnimalQueryMenu __instance, FarmAnimal animal) {
        var mod = Mod.Instance;

        var hearts = mod.GetAnimalHearts(animal);
        Mod.queryMenuOverflowHearts.Add(__instance, new(hearts));

        AnimalQueryMenu.height = 512;
        if (mod.config.ShowAnimalHearts && hearts != 0) AnimalQueryMenu.height += 28;
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
                new(OpCodes.Pop),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, AccessTools.Method(
                    typeof(Mod), nameof(Mod.patch_AnimalQueryMenu_new_height)
                )),
            ])
            .Advance(1)
        )
        .InstructionEnumeration();

    static int patch_AnimalQueryMenu_new_height(AnimalQueryMenu menu) => 512 + (
        Mod.Instance.config.ShowAnimalHearts &&
        Mod.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ?
        28 : 0
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
                        typeof(Mod), nameof(Mod.patch_AnimalQueryMenu_draw_offset)
                    )),
                ])
            )
            .InstructionEnumeration();
    }

    static int patch_AnimalQueryMenu_draw_offset(AnimalQueryMenu menu, int value) => value + (
        Mod.Instance.config.ShowAnimalHearts &&
        Mod.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ?
        28 : 0
    );

    static void postfix_AnimalQueryMenu_draw(AnimalQueryMenu __instance, SpriteBatch b) {
        var mod = Mod.Instance;

        if (mod.config.ShowAnimalHearts) {
            var hearts = Mod.queryMenuOverflowHearts.GetValue(__instance, _ => new(0)).Value;
            if (hearts != 0) {
                var parentOffset = __instance.parentName is null ? 0 : 21;
                mod.drawHearts(b, hearts, 15, new(
                    __instance.xPositionOnScreen + 252,
                    __instance.yPositionOnScreen + parentOffset + 288
                ));
            }
        }
    }

    void drawHearts(SpriteBatch b, BigInteger hearts, int width, Vector2 at) {
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

        var text = $"{hearts:+#;-#;0}×";
        var overlong = text.Length > width;
        if (overlong) text = text.Substring(0, width - 1) + "×";

        foreach (var (c, i) in text.Reverse().Select((c, i) => (c, i))) {
            var digit = c switch { '+' => 10, '-' => 11, '×' => 12, _ => overlong ? 9 : c - '0' };
            b.Draw(
                texture: this.font,
                position: at - new Vector2(41 + i * 12, -3),
                sourceRectangle: new(digit * 3, 0, 3, 5),
                color: this.config.TextColorOverride?.AsColor() ?? Game1.textColor,
                rotation: 0,
                origin: Vector2.Zero,
                scale: 3,
                effects: SpriteEffects.None,
                layerDepth: 0.88f
            );
        }
    }

    static void postfix_SocialPage_FindSocialCharacters(List<SocialPage.SocialEntry> __result) {
        var mod = Mod.Instance;

        if (mod.config.ShowNpcHearts) {
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
        var mod = Mod.Instance;

        if (mod.config.ShowAnimalHearts) {
            Utils.SortGroups<AnimalPage.AnimalEntry, int, BigInteger>(
                __result,
                entry => entry.Animal is FarmAnimal a ? a.friendshipTowardFarmer.Value : null,
                entry => -Mod.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value
            );
        }
    }
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

sealed class Config {
    public bool ShowNpcHearts { get; set; } = true;

    public bool ShowAnimalHearts { get; set; } = true;

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

static class Utils {
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
