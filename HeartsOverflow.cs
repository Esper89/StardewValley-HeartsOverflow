using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using StardewModdingAPI;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;
using Netcode;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

internal sealed class Mod : StardewModdingAPI.Mod
{
    public override void Entry(IModHelper helper)
    {
        Mod.instance = this;
        this.config = helper.ReadConfig<Config>();
        this.font = Texture2D.FromStream(Game1.graphics.GraphicsDevice, Mod.Asset("font.png"));

        helper.Events.GameLoop.GameLaunched += (_, _) => this.OnGameLaunched();

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.DeclaredMethod(typeof(Farmer), nameof(Farmer.changeFriendship)),
            transpiler: new HarmonyMethod(
                typeof(Mod), nameof(Mod.transpile_Farmer_changeFriendship)
            )
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Get)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_NetFieldBase_int_NetInt_Get)
            )
        );
        harmony.Patch(
            original: AccessTools.DeclaredPropertyGetter(
                typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Value)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_NetFieldBase_int_NetInt_Value_get)
            )
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)]
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_Math_Min_int_int))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(typeof(FarmAnimal), "initNetFields"),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_FarmAnimal_initNetFields))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(typeof(Pet), "initNetFields"),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_Pet_initNetFields))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(SocialPage), nameof(SocialPage.drawNPCSlot)
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_SocialPage_drawNPCSlot))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(typeof(ProfileMenu), "drawNPCSlotHeart"),
            prefix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.prefix_ProfileMenu_drawNPCSlotHeart)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_ProfileMenu_drawNPCSlotHeart)
            )
        );
        harmony.Patch(
            original: AccessTools.DeclaredConstructor(
                typeof(AnimalPage.AnimalEntry), [typeof(Character)]
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_AnimalEntry_new))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(typeof(AnimalPage), "drawNPCSlot"),
            transpiler: new HarmonyMethod(
                typeof(Mod), nameof(Mod.transpile_AnimalPage_drawNPCSlot)
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_AnimalPage_drawNPCSlot))
        );
        harmony.Patch(
            original: AccessTools.DeclaredConstructor(
                typeof(AnimalQueryMenu), [typeof(FarmAnimal)]
            ),
            prefix: new HarmonyMethod(typeof(Mod), nameof(Mod.prefix_AnimalQueryMenu_new)),
            transpiler: new HarmonyMethod(typeof(Mod), nameof(Mod.transpile_AnimalQueryMenu_new))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(AnimalQueryMenu), nameof(AnimalQueryMenu.draw)
            ),
            transpiler: new HarmonyMethod(typeof(Mod), nameof(Mod.transpile_AnimalQueryMenu_draw)),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_AnimalQueryMenu_draw))
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(SocialPage), nameof(SocialPage.FindSocialCharacters)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_SocialPage_FindSocialCharacters)
            )
        );
        harmony.Patch(
            original: AccessTools.DeclaredMethod(
                typeof(AnimalPage), nameof(AnimalPage.FindAnimals)
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_AnimalPage_FindAnimals))
        );
    }

    private void OnGameLaunched()
    {
        var gmcm = this.Helper.ModRegistry.GetApi<GenericModConfigMenu.IGenericModConfigMenuApi>(
            "spacechase0.GenericModConfigMenu"
        );
        var gmcmExt = this.Helper.ModRegistry.GetApi<GMCMOptions.IGMCMOptionsAPI>(
            "jltaylor-us.GMCMOptions"
        );

        if (gmcm is not null)
        {
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
            if (gmcmExt is not null)
            {
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    getValue: () => this.config.TextColorOverride is not null,
                    setValue: value => this.config.TextColorOverride = value
                        ? new(Game1.textColor)
                        : null,
                    name: () => this.Helper.Translation.Get("config.override-text-color.name"),
                    tooltip: () => this.Helper.Translation.Get("config.override-text-color.desc")
                );
                gmcmExt.AddColorOption(
                    mod: this.ModManifest,
                    getValue: () => this.config.TextColorOverride?.AsColor() ?? Game1.textColor,
                    setValue: value => this.config.TextColorOverride?.SetColor(value),
                    name: () => this.Helper.Translation.Get("config.text-color-override.name"),
                    tooltip: () => this.Helper.Translation.Get("config.text-color-override.desc")
                );
            }
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.SocialPageOffset.X,
                setValue: value => this.config.SocialPageOffset.X = value,
                name: () => this.Helper.Translation.Get("config.social-page-offset.x.name"),
                tooltip: () => this.Helper.Translation.Get("config.social-page-offset.x.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.SocialPageOffset.Y,
                setValue: value => this.config.SocialPageOffset.Y = value,
                name: () => this.Helper.Translation.Get("config.social-page-offset.y.name"),
                tooltip: () => this.Helper.Translation.Get("config.social-page-offset.y.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.ProfileMenuOffset.X,
                setValue: value => this.config.ProfileMenuOffset.X = value,
                name: () => this.Helper.Translation.Get("config.profile-menu-offset.x.name"),
                tooltip: () => this.Helper.Translation.Get("config.profile-menu-offset.x.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.ProfileMenuOffset.Y,
                setValue: value => this.config.ProfileMenuOffset.Y = value,
                name: () => this.Helper.Translation.Get("config.profile-menu-offset.y.name"),
                tooltip: () => this.Helper.Translation.Get("config.profile-menu-offset.y.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.AnimalPageOffset.X,
                setValue: value => this.config.AnimalPageOffset.X = value,
                name: () => this.Helper.Translation.Get("config.animal-page-offset.x.name"),
                tooltip: () => this.Helper.Translation.Get("config.animal-page-offset.x.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.AnimalPageOffset.Y,
                setValue: value => this.config.AnimalPageOffset.Y = value,
                name: () => this.Helper.Translation.Get("config.animal-page-offset.y.name"),
                tooltip: () => this.Helper.Translation.Get("config.animal-page-offset.y.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.QueryMenuOffset.X,
                setValue: value => this.config.QueryMenuOffset.X = value,
                name: () => this.Helper.Translation.Get("config.query-menu-offset.x.name"),
                tooltip: () => this.Helper.Translation.Get("config.query-menu-offset.x.desc")
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.config.QueryMenuOffset.Y,
                setValue: value => this.config.QueryMenuOffset.Y = value,
                name: () => this.Helper.Translation.Get("config.query-menu-offset.y.name"),
                tooltip: () => this.Helper.Translation.Get("config.query-menu-offset.y.desc")
            );
        }
    }

    private static Stream? Asset(string assetFile) => typeof(Mod).Assembly
        .GetManifestResourceStream($"{nameof(HeartsOverflow)}.assets.{assetFile}");

    private static Mod? instance;
    private Config config = new();
    private Texture2D? font;

    private static string modDataKey()
        => $"Esper89.HeartsOverflow.OverflowFriendshipTowardFarmer";

    private static string modDataKey(Farmer player)
        => $"Esper89.HeartsOverflow.OverflowFriendshipPoints[{player.UniqueMultiplayerID}]";

    private static BigInteger parsePoints(Character c, string key)
        => c.modData.TryGetValue(key, out string data)
            ? BigInteger.TryParse(data, out BigInteger points) ? points : BigInteger.Zero
            : BigInteger.Zero;

    private static BigInteger getPoints(Character c) => Mod.parsePoints(c, Mod.modDataKey());

    private static BigInteger getPoints(Character c, Farmer player)
        => Mod.parsePoints(c, Mod.modDataKey(player));

    private static void addPoints(Character c, int points)
    {
        var s = points == 1 ? "" : "s";
        Mod.instance!.Monitor.Log(
            $"Friendship with {c.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = Mod.modDataKey();
        c.modData[key] = (Mod.parsePoints(c, key) + points).ToString();
    }

    private static void addPoints(Character c, Farmer player, int points)
    {
        var posessive = player.Name.EndsWith('s') ? "'" : "'s";
        var s = points == 1 ? "" : "s";
        Mod.instance!.Monitor.Log(
            $"{player.Name}{posessive} friendship with {c.Name} overflowed by {points} point{s}",
            LogLevel.Trace
        );

        var key = Mod.modDataKey(player);
        c.modData[key] = (Mod.parsePoints(c, key) + points).ToString();
    }

    private static BigInteger getHearts(Character c)
    {
        var points = Mod.getPoints(c);
        if (points < 0 && points % 250 != 0) return points / 250 - 1;
        else return points / 250;
    }

    private static BigInteger getHearts(Character c, Farmer player)
    {
        var points = Mod.getPoints(c, player) + 249;
        if (points < 0 && points % 250 != 0) return points / 250 - 1;
        else return points / 250;
    }

    private static IEnumerable<CodeInstruction> transpile_Farmer_changeFriendship(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Call, AccessTools.DeclaredMethod(
                typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)]
            )),
        ])
        .ThrowIfNotMatch(
            $"Could not transpile {typeof(Farmer)}.{nameof(Farmer.changeFriendship)}: method " +
            $"does not call {typeof(Math)}.{nameof(Math.Min)}({typeof(int)}, {typeof(int)})"
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

    private static int patch_Farmer_changeFriendship_Math_Min(
        int total, int max,
        Farmer player, int amount, NPC npc
    )
    {
        var overflow = total - max;
        if (overflow > 0 && amount > 0) Mod.addPoints(npc, player, overflow);
        return Math.Min(total, max);
    }

    private static ThreadLocal<NetInt?> lastNetIntAccessed = new(() => null);
    private static ThreadLocal<int?> lastMinWith1000 = new(() => null);

    private static void postfix_NetFieldBase_int_NetInt_Get(NetFieldBase<int, NetInt> __instance)
    {
        if (__instance is NetInt netInt)
        {
            Mod.lastNetIntAccessed.Value = netInt;
            Mod.lastMinWith1000.Value = null;
        }
    }

    private static void postfix_NetFieldBase_int_NetInt_Value_get(
        NetFieldBase<int, NetInt> __instance
    ) => Mod.postfix_NetFieldBase_int_NetInt_Get(__instance);

    private static void postfix_Math_Min_int_int(int val1, int val2)
    {
        if (Mod.lastNetIntAccessed.Value is not null)
        {
            if (val1 == 1000) Mod.lastMinWith1000.Value = val2;
            else if (val2 == 1000) Mod.lastMinWith1000.Value = val1;
        }
    }

    private static void postfix_FarmAnimal_initNetFields(FarmAnimal __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent += (netInt, oldValue, newValue)
            => Mod.animalFriendshipChanged(__instance, netInt, oldValue, newValue);

    private static void postfix_Pet_initNetFields(Pet __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent += (netInt, oldValue, newValue)
            => Mod.animalFriendshipChanged(__instance, netInt, oldValue, newValue);

    private static void animalFriendshipChanged(
        Character animal, NetInt friendshipTowardFarmer,
        int oldValue, int newValue
    )
    {
        if (
            NetInt.ReferenceEquals(Mod.lastNetIntAccessed.Value, friendshipTowardFarmer) &&
            Mod.lastMinWith1000.Value is int valueBeforeMin
        )
        {
            Mod.lastNetIntAccessed.Value = null;
            Mod.lastMinWith1000.Value = null;
            if (newValue >= oldValue && valueBeforeMin > 1000 && newValue == 1000)
            {
                Mod.addPoints(animal, valueBeforeMin - 1000);
            }
        }
    }

    private static void postfix_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i)
    {
        if (Mod.instance!.config.ShowNpcHearts)
        {
            var hearts = Mod.getHearts(__instance.GetSocialEntry(i).Character, Game1.player);
            if (hearts != 0)
            {
                var offset = Mod.instance!.config.SocialPageOffset;
                Mod.drawHearts(b, hearts, 24, new(
                    __instance.xPositionOnScreen + 632 + offset.X,
                    __instance.sprites[i].bounds.Y + 8 + offset.Y
                ));
            }
        }
    }

    private static void prefix_ProfileMenu_drawNPCSlotHeart(
        ref float heartDrawStartY,
        SocialPage.SocialEntry entry
    )
    {
        if (Mod.instance!.config.ShowNpcHearts)
        {
            var overflowHearts = Mod.getHearts(entry.Character, Game1.player);
            if (overflowHearts != 0 && heartDrawStartY >= 0) heartDrawStartY += 16;
        }
    }

    private static void postfix_ProfileMenu_drawNPCSlotHeart(
        ProfileMenu __instance, SpriteBatch b,
        float heartDrawStartX, float heartDrawStartY,
        SocialPage.SocialEntry entry,
        int hearts
    )
    {
        if (Mod.instance!.config.ShowNpcHearts)
        {
            var overflowHearts = Mod.getHearts(entry.Character, Game1.player);
            if (hearts == 0 && overflowHearts != 0)
            {
                var heartDisplayPosition = AccessTools.FieldRefAccess<ProfileMenu, Vector2>(
                    __instance, "_heartDisplayPosition"
                );

                var below = heartDrawStartY < 0;
                var offset = Mod.instance!.config.ProfileMenuOffset;
                Mod.drawHearts(b, overflowHearts, below ? 13 : 26, new(
                    heartDrawStartX + 316 + offset.X,
                    heartDisplayPosition.Y + heartDrawStartY + (below ? 32 : -32) + offset.Y
                ));
            }
        }
    }

    private static ConditionalWeakTable<AnimalPage.AnimalEntry, Utils.Box<BigInteger>>
        animalEntryOverflowHearts = new();

    private static void postfix_AnimalEntry_new(AnimalPage.AnimalEntry __instance)
        => Mod.animalEntryOverflowHearts.Add(__instance, new(Mod.getHearts(__instance.Animal)));

    private static IEnumerable<CodeInstruction> transpile_AnimalPage_drawNPCSlot(
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

    private static bool patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker(
        AnimalPage.AnimalEntry entry,
        bool value
    ) => value && !Mod.instance!.config.ShowAnimalHearts;

    private static void postfix_AnimalPage_drawNPCSlot(AnimalPage __instance, SpriteBatch b, int i)
    {
        if (Mod.instance!.config.ShowAnimalHearts)
        {
            var entry = __instance.GetSocialEntry(i);
            var hearts = Mod.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value;
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
                layerDepth: 0.8f
            );

            if (hearts != 0)
            {
                var offset = Mod.instance!.config.AnimalPageOffset;
                Mod.drawHearts(b, hearts, 11, new(
                    __instance.xPositionOnScreen + 664 + offset.X,
                    __instance.sprites[i].bounds.Y + heightOffset + 12 + offset.Y
                ));
            }
        }
    }

    private static ConditionalWeakTable<AnimalQueryMenu, Utils.Box<BigInteger>>
        queryMenuOverflowHearts = new();

    private static void prefix_AnimalQueryMenu_new(AnimalQueryMenu __instance, FarmAnimal animal)
    {
        var hearts = Mod.getHearts(animal);
        Mod.queryMenuOverflowHearts.Add(__instance, new(hearts));

        AnimalQueryMenu.height = 512;
        if (Mod.instance!.config.ShowAnimalHearts && hearts != 0) AnimalQueryMenu.height += 28;
    }

    private static IEnumerable<CodeInstruction> transpile_AnimalQueryMenu_new(
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

    private static int patch_AnimalQueryMenu_new_height(AnimalQueryMenu menu)
        => 512 + (
            Mod.instance!.config.ShowAnimalHearts &&
            Mod.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ?
            28 : 0
        );

    private static IEnumerable<CodeInstruction> transpile_AnimalQueryMenu_draw(
        IEnumerable<CodeInstruction> instructions
    )
    {
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
                $"Could not transpile {typeof(AnimalQueryMenu)}.{nameof(AnimalQueryMenu.draw)}: " +
                $"method does not assert that {typeof(AnimalQueryMenu)}." +
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

    private static int patch_AnimalQueryMenu_draw_offset(AnimalQueryMenu menu, int value)
        => value + (
            Mod.instance!.config.ShowAnimalHearts &&
            Mod.queryMenuOverflowHearts.GetValue(menu, _ => new(0)).Value != 0 ?
            28 : 0
        );

    private static void postfix_AnimalQueryMenu_draw(AnimalQueryMenu __instance, SpriteBatch b)
    {
        if (Mod.instance!.config.ShowAnimalHearts)
        {
            var hearts = Mod.queryMenuOverflowHearts.GetValue(__instance, _ => new(0)).Value;
            if (hearts != 0)
            {
                var offset = Mod.instance!.config.ProfileMenuOffset;
                var parentOffset = __instance.parentName is null ? 0 : 21;
                Mod.drawHearts(b, hearts, 15, new(
                    __instance.xPositionOnScreen + 252 + offset.X,
                    __instance.yPositionOnScreen + parentOffset + 288 + offset.Y
                ));
            }
        }
    }

    private static void drawHearts(SpriteBatch b, BigInteger hearts, int width, Vector2 at)
    {
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

        foreach (var (c, i) in text.Reverse().Select((c, i) => (c, i)))
        {
            var digit = c switch { '+' => 10, '-' => 11, '×' => 12, _ => overlong ? 9 : c - '0' };
            b.Draw(
                texture: Mod.instance!.font,
                position: at - new Vector2(41 + i * 12, -3),
                sourceRectangle: new(digit * 3, 0, 3, 5),
                color: Mod.instance!.config.TextColorOverride?.AsColor() ?? Game1.textColor,
                rotation: 0,
                origin: Vector2.Zero,
                scale: 3,
                effects: SpriteEffects.None,
                layerDepth: 0.88f
            );
        }
    }

    private static void postfix_SocialPage_FindSocialCharacters(
        List<SocialPage.SocialEntry> __result
    )
    {
        if (Mod.instance!.config.ShowNpcHearts)
        {
            Utils.SortGroups<SocialPage.SocialEntry, int, BigInteger>(
                __result,
                entry => !entry.IsPlayer && !entry.IsChild ? entry.Friendship?.Points ?? 0 : null,
                entry => -Mod.getPoints(entry.Character, Game1.player)
            );
        }
    }

    private static void postfix_AnimalPage_FindAnimals(List<AnimalPage.AnimalEntry> __result)
    {
        if (Mod.instance!.config.ShowAnimalHearts)
        {
            Utils.SortGroups<AnimalPage.AnimalEntry, int, BigInteger>(
                __result,
                entry => entry.Animal is FarmAnimal a ? a.friendshipTowardFarmer.Value : null,
                entry => -Mod.animalEntryOverflowHearts.GetValue(entry, _ => new(0)).Value
            );
        }
    }
}

internal sealed class Config
{
    public bool ShowNpcHearts { get; set; } = true;

    public bool ShowAnimalHearts { get; set; } = true;

    public TextColor? TextColorOverride { get; set; } = null;

    public Offset SocialPageOffset { get; set; } = new(0, 0);

    public Offset ProfileMenuOffset { get; set; } = new(0, 0);

    public Offset AnimalPageOffset { get; set; } = new(0, 0);

    public Offset QueryMenuOffset { get; set; } = new(0, 0);

    internal sealed class TextColor
    {
        public TextColor(byte r, byte g, byte b, byte a)
        {
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

        internal void SetColor(Color color)
        {
            this.R = color.R;
            this.G = color.G;
            this.B = color.B;
            this.A = color.A;
        }
    }

    internal sealed class Offset
    {
        public Offset(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }

        public int X { get; set; }
        public int Y { get; set; }
    }
}

internal static class Utils
{
    internal static void SortGroups<T, G, K>(IList<T> list, Func<T, G?> group, Func<T, K> key)
    where G : struct, IEquatable<G> where K : IComparable<K>
    {
        int? sort = null;
        G? prev = null;
        for (int i = 0; i < list.Count; i++)
        {
            var curr = group(list[i]);

            if (curr is not null && curr.Equals(prev)) sort ??= i - 1;
            else if (sort is not null)
            {
                Utils.SortStable(list, (int)sort, i, key);
                sort = null;
            }

            prev = curr;
        }

        if (sort is not null) Utils.SortStable(list, (int)sort, list.Count, key);
    }

    internal static void SortStable<T, K>(IList<T> list, int start, int end, Func<T, K> key)
    where K : IComparable<K>
    {
        for (int i = start + 1; i < end; i++)
        {
            var v = list[i];
            var k = key(v);

            int j;
            for (j = i - 1; j >= start; j--)
            {
                if (k.CompareTo(key(list[j])) >= 0) break;
                list[j + 1] = list[j];
            }

            list[j + 1] = v;
        }
    }

    internal static class OpCodeSets
    {
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

    internal static CodeInstruction StlocToLdloc(this CodeInstruction instr) => instr.opcode switch
    {
        var op when op == OpCodes.Stloc_0 => new(OpCodes.Ldloc_0),
        var op when op == OpCodes.Stloc_1 => new(OpCodes.Ldloc_1),
        var op when op == OpCodes.Stloc_2 => new(OpCodes.Ldloc_2),
        var op when op == OpCodes.Stloc_3 => new(OpCodes.Ldloc_3),
        var op when op == OpCodes.Stloc_S => new(OpCodes.Ldloc_S, instr.operand),
        var op when op == OpCodes.Stloc => new(OpCodes.Ldloc, instr.operand),
        _ => throw new ArgumentException("invalid opcode", nameof(instr)),
    };

    internal class Box<T>
    {
        internal Box(T value) => this.Value = value;
        internal T Value;
    }
}
