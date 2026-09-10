using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using StardewModdingAPI;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Characters;
using StardewValley.Menus;
using Netcode;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

static class Patches {
    internal static void Apply(Mod mod) {
        var patcher = new Patcher(new(mod.ModManifest.UniqueID), mod.Monitor);

        patcher.PatchGetter(
            typeof(Game1), nameof(Game1.player),
            postfix: nameof(Patches.postfix_Game1_player_get)
        );
        patcher.PatchMethod(
            typeof(Farmer), nameof(Farmer.changeFriendship),
            transpiler: nameof(Patches.transpile_Farmer_changeFriendship)
        );
        patcher.PatchMethod(
            typeof(Farmer), nameof(Farmer.doDivorce),
            prefix: nameof(Patches.prefix_Farmer_doDivorce)
        );
        patcher.PatchMethod(
            typeof(Farmer), "initNetFields",
            postfix: nameof(Patches.postfix_Farmer_initNetFields)
        );
        patcher.PatchConstructor(
            typeof(Friendship), [],
            postfix: nameof(Patches.postfix_Friendship_new)
        );
        patcher.PatchMethod(
            typeof(Friendship), nameof(Friendship.Clear),
            postfix: nameof(Patches.prefix_Friendship_Clear)
        );
        patcher.PatchMethod(
            typeof(NPC), nameof(NPC.tryToReceiveActiveObject),
            transpiler: nameof(transpile_NPC_tryToReceiveActiveObject)
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
            typeof(Math), nameof(Math.Max), [typeof(int), typeof(int)],
            postfix: nameof(Patches.postfix_Math_Max_int_int)
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

    internal static void ClearThreadState() {
        Patches.lastFriendshipTowardFarmerAccessed.Value = null;
        Patches.lastFriendshipTowardFarmerBeforeMin.Value = null;
        Patches.lastFriendshipTowardFarmerBeforeMax.Value = null;
    }

    static ThreadLocal<Farmer?> game1PlayerOverride = new(() => null);

    static void postfix_Game1_player_get(ref Farmer __result) {
        var player = Patches.game1PlayerOverride.Value;
        if (player is not null) __result = player;
    }

    internal static int GetNpcMaxHearts(Farmer player, NPC npc) {
        var prev = Patches.game1PlayerOverride.Value;
        try {
            Patches.game1PlayerOverride.Value = player;
            return Utility.GetMaximumHeartsForCharacter(npc);
        } finally {
            Patches.game1PlayerOverride.Value = prev;
        }
    }

    static IEnumerable<CodeInstruction> transpile_Farmer_changeFriendship(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator
    ) {
        var setPointsTo = generator.DeclareLocal(typeof(int));
        var friendshipPointsSetter = AccessTools.DeclaredPropertySetter(
            typeof(Friendship), nameof(Friendship.Points)
        );
        return new CodeMatcher(instructions, generator)
            .MatchStartForward([
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Callvirt, friendshipPointsSetter),
            ])
            .Repeat(matcher => matcher
                .SetOpcodeAndAdvance(OpCodes.Pop)
                .RemoveInstruction()
            )
            .Start()
            .MatchStartForward([
                new(OpCodes.Callvirt, friendshipPointsSetter),
            ])
            .ThrowIfNotMatch(
                $"could not transpile method: does not set {typeof(Friendship)}" +
                $".{nameof(Friendship.Points)}"
            )
            .Repeat(matcher => matcher
                .InsertAndAdvance([
                    new(OpCodes.Stloc, setPointsTo),
                    new(OpCodes.Dup),
                    new(OpCodes.Ldloc, setPointsTo),
                    new(OpCodes.Ldarg_0),
                    new(OpCodes.Ldarg_1),
                    new(OpCodes.Ldarg_2),
                    CodeInstruction.Call(
                        typeof(Patches),
                        nameof(Patches.patch_Farmer_changeFriendship_Friendship_Points_set)
                    ),
                ])
                .Advance(1)
            )
            .InstructionEnumeration();
    }

    static int patch_Farmer_changeFriendship_Friendship_Points_set(
        Friendship? friendship, int points, Farmer player, int amount, NPC? npc
    ) {
        if (friendship is not null && npc is not null) {
            points = Mod.Instance.ChangeNpcPoints(player, npc, friendship.Points, points, amount);
        }

        return points;
    }

    static void prefix_Farmer_doDivorce(Farmer __instance) {
        var spouse = __instance.getSpouse();
        if (spouse is not null) {
            var mod = Mod.Instance;
            if (mod.GetTotalNpcPoints(__instance, spouse) > 0) {
                mod.ClearNpcOverflow(__instance, spouse);
            }
        }
    }

    static void postfix_Farmer_initNetFields(Farmer __instance) {
        var mod = Mod.Instance;
        __instance.friendshipData.OnValueAdded += (npcName, _) => {
            Patches.forEachAllowedPlayer(player => {
                if (Farmer.ReferenceEquals(player, __instance)) {
                    Patches.findNpc(npcName, npc => mod.SyncNpcFriendshipBounds(player, npc));
                    return false;
                } else return true;
            });
        };
    }

    static void postfix_Friendship_new(Friendship __instance) {
        var mod = Mod.Instance;

        var status = AccessTools.FieldRefAccess<Friendship, NetEnum<FriendshipStatus>>(
            __instance, "status"
        );
        status.fieldChangeEvent += (_, _, _) => {
            Patches.forEachAllowedPlayer(player => {
                foreach (var npcName in Patches.findFriendshipFor(player, __instance)) {
                    Patches.findNpc(npcName, npc => mod.SyncNpcFriendshipBounds(player, npc));
                }
                return true;
            });
        };
    }

    static void prefix_Friendship_Clear(Friendship __instance) {
        var mod = Mod.Instance;
        foreach (var (player, npcName) in Patches.findFriendship(__instance)) {
            mod.ClearNpcOverflowByName(player, npcName);
        }
    }

    static void forEachAllowedPlayer(Func<Farmer, bool> action) {
        if (action(Game1.player) && Context.IsMainPlayer) {
            foreach (var player in Game1.getOfflineFarmhands()) {
                if (!action(player)) break;
            }
        }
    }

    static IEnumerable<(Farmer, string)> findFriendship(Friendship friendship) {
        foreach (var player in Game1.getAllFarmers()) {
            foreach (var npcName in Patches.findFriendshipFor(player, friendship)) {
                yield return (player, npcName);
            }
        }
    }

    static IEnumerable<string> findFriendshipFor(Farmer player, Friendship friendship) {
        foreach (var p in player.friendshipData.Pairs) {
            if (Friendship.ReferenceEquals(p.Value, friendship)) {
                yield return p.Key;
            }
        }
    }

    static void findNpc(string npcName, Action<NPC> action) {
        var hit = false;
        Utility.ForEachLocation(
            location => {
                if (location.IsActiveLocation()) {
                    foreach (var npc in location.characters) {
                        if (npc.Name == npcName) {
                            hit = true;
                            action(npc);
                        }
                    }
                }

                return true;
            },
            includeGenerated: true
        );

        if (!hit) {
            Utility.ForEachLocation(
                location => {
                    if (!location.IsActiveLocation()) {
                        foreach (var npc in location.characters) {
                            if (npc.Name == npcName) {
                                action(npc);
                            }
                        }
                    }

                    return true;
                },
                includeGenerated: true
            );
        }
    }

    static IEnumerable<CodeInstruction> transpile_NPC_tryToReceiveActiveObject(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Ldstr, "Strings\\StringsFromCSFiles:Wilted_Bouquet_Effect"),
        ])
        .ThrowIfNotMatch(
            "could not transpile method: does not contain string " +
            @"""Strings\\StringsFromCSFiles:Wilted_Bouquet_Effect"""
        )
        .Repeat(matcher => matcher
            .InsertAndAdvance([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldarg_1),
                CodeInstruction.Call(
                    typeof(Patches),
                    nameof(Patches.patch_NPC_tryToReceiveActiveObject_WiltedBouquet)
                ),
            ])
            .Advance(1)
        )
        .InstructionEnumeration();

    static void patch_NPC_tryToReceiveActiveObject_WiltedBouquet(NPC npc, Farmer player) {
        var mod = Mod.Instance;
        if (mod.GetTotalNpcPoints(player, npc) > 1250) {
            mod.ClearNpcOverflow(player, npc);
        }
    }

    static ConditionalWeakTable<NetInt, Character> animalFriendshipTowardFarmerField = new();

    static void postfix_FarmAnimal_initNetFields(FarmAnimal __instance)
        => Patches.animalFriendshipTowardFarmerField
            .Add(__instance.friendshipTowardFarmer, __instance);

    static void postfix_Pet_initNetFields(Pet __instance)
        => Patches.animalFriendshipTowardFarmerField
            .Add(__instance.friendshipTowardFarmer, __instance);

    internal static int GetNetIntPure(NetInt netInt)
        => AccessTools.FieldRefAccess<NetInt, int>(netInt, "value");

    internal static void SetNetIntPure(NetInt netInt, int value) {
        var prev = Patches.netIntPureSet.Value;
        try {
            Patches.netIntPureSet.Value = true;
            netInt.Set(value);
        } finally {
            Patches.netIntPureSet.Value = prev;
        }
    }

    static ThreadLocal<bool> netIntPureSet = new(() => false);

    static ThreadLocal<NetInt?> lastFriendshipTowardFarmerAccessed = new(() => null);
    static ThreadLocal<int?> lastFriendshipTowardFarmerBeforeMin = new(() => null);
    static ThreadLocal<int?> lastFriendshipTowardFarmerBeforeMax = new(() => null);

    static void postfix_NetFieldBase_int_NetInt_Get(NetFieldBase<int, NetInt> __instance) {
        if (
            __instance is NetInt netInt &&
            Patches.animalFriendshipTowardFarmerField.TryGetValue(netInt, out _)
        ) {
            Patches.lastFriendshipTowardFarmerAccessed.Value = netInt;
            Patches.lastFriendshipTowardFarmerBeforeMin.Value = null;
            Patches.lastFriendshipTowardFarmerBeforeMax.Value = null;
        }
    }

    static void postfix_NetFieldBase_int_NetInt_Value_get(NetFieldBase<int, NetInt> __instance)
        => Patches.postfix_NetFieldBase_int_NetInt_Get(__instance);

    static void postfix_Math_Min_int_int(int val1, int val2) {
        if (
            Patches.lastFriendshipTowardFarmerAccessed.Value is NetInt last &&
            Patches.animalFriendshipTowardFarmerField.TryGetValue(last, out var animal)
        ) {
            var max = Mod.Instance.AnimalMaxFriendship(animal);
            if (val1 == max) Patches.lastFriendshipTowardFarmerBeforeMin.Value = val2;
            else if (val2 == max) Patches.lastFriendshipTowardFarmerBeforeMin.Value = val1;
        }
    }

    static void postfix_Math_Max_int_int(int val1, int val2) {
        if (
            Patches.lastFriendshipTowardFarmerAccessed.Value is NetInt last &&
            Patches.animalFriendshipTowardFarmerField.TryGetValue(last, out var animal)
        ) {
            var min = Mod.Instance.AnimalMinFriendship(animal);
            if (val1 == min) Patches.lastFriendshipTowardFarmerBeforeMax.Value = val2;
            else if (val2 == min) Patches.lastFriendshipTowardFarmerBeforeMax.Value = val1;
        }
    }

    static void prefix_NetInt_Set(NetInt __instance, ref int newValue) {
        if (
            !Patches.netIntPureSet.Value &&
            Patches.animalFriendshipTowardFarmerField.TryGetValue(__instance, out var animal)
        ) {
            var mod = Mod.Instance;
            var min = mod.AnimalMinFriendship(animal);
            var max = mod.AnimalMaxFriendship(animal);

            var oldValue = Patches.GetNetIntPure(__instance);
            var diff = newValue - oldValue;

            if (
                newValue == min &&
                newValue <= oldValue &&
                Patches.lastFriendshipTowardFarmerBeforeMax.Value is int valueBeforeMax &&
                valueBeforeMax <= min
            ) diff = valueBeforeMax - oldValue;
            else if (
                newValue == max &&
                newValue >= oldValue &&
                Patches.lastFriendshipTowardFarmerBeforeMin.Value is int valueBeforeMin &&
                valueBeforeMin >= max
            ) diff = valueBeforeMin - oldValue;

            newValue = mod.ChangeAnimalPoints(animal, oldValue, newValue, diff);

            Patches.lastFriendshipTowardFarmerAccessed.Value = null;
            Patches.lastFriendshipTowardFarmerBeforeMin.Value = null;
            Patches.lastFriendshipTowardFarmerBeforeMax.Value = null;
        }
    }

    static ConditionalWeakTable<SocialPage.SocialEntry, Utils.Box<BigInteger>>
        socialEntryOverflowHearts = new();

    static void postfix_SocialEntry_new(SocialPage.SocialEntry __instance) {
        if (__instance.Character is NPC npc) {
            var hearts = Mod.Instance.GetOverflowNpcHearts(Game1.player, npc);
            if (hearts != 0) Patches.socialEntryOverflowHearts.Add(__instance, new(hearts));
        }
    }

    static void postfix_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i) {
        var entry = __instance.GetSocialEntry(i);
        var hearts = Patches.socialEntryOverflowHearts.GetFromBox(entry, () => 0);
        if (hearts != 0) Mod.Instance.DrawOverflowHearts(b, hearts, 24, new(
            __instance.xPositionOnScreen + 632,
            __instance.sprites[i].bounds.Y + 8
        ));
    }

    static void prefix_ProfileMenu_drawNPCSlotHeart(
        ref float heartDrawStartY,
        SocialPage.SocialEntry entry
    ) {
        var hearts = Patches.socialEntryOverflowHearts.GetFromBox(entry, () => 0);
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
        var overflowHearts = Patches.socialEntryOverflowHearts.GetFromBox(entry, () => 0);
        if (overflowHearts != 0) {
            var heartDisplayPosition = AccessTools.FieldRefAccess<ProfileMenu, Vector2>(
                __instance, "_heartDisplayPosition"
            );

            var width = Utility.GetMaximumHeartsForCharacter(entry.Character) switch {
                <= 10 => 26,
                11 => 21, 12 => 18, 13 => 15, 14 => 13, 15 => 10, 16 => 7, 17 => 5, 18 => 2,
                > 18 => 0,
            };

            Mod.Instance.DrawOverflowHearts(b, overflowHearts, width, new(
                heartDrawStartX + 316,
                heartDisplayPosition.Y + heartDrawStartY + 32
            ));
        }
    }

    static ConditionalWeakTable<AnimalPage.AnimalEntry, Utils.Box<BigInteger>>
        animalEntryOverflowHearts = new();

    static void postfix_AnimalEntry_new(AnimalPage.AnimalEntry __instance) {
        var hearts = Mod.Instance.GetOverflowAnimalHearts(__instance.Animal);
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
                CodeInstruction.Call(
                    typeof(Patches),
                    nameof(Patches.patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker)
                ),
            ])
        )
        .InstructionEnumeration();

    static bool patch_AnimalPage_drawNPCSlot_ReceivedAnimalCracker(
        AnimalPage.AnimalEntry entry,
        bool value
    ) => value && Patches.animalEntryOverflowHearts.GetFromBox(entry, () => 0) == 0;

    static void postfix_AnimalPage_drawNPCSlot(AnimalPage __instance, SpriteBatch b, int i) {
        var entry = __instance.GetSocialEntry(i);
        var hearts = Patches.animalEntryOverflowHearts.GetFromBox(entry, () => 0);
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

            Mod.Instance.DrawOverflowHearts(b, hearts, 11, new(
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

        var hearts = Mod.Instance.GetOverflowAnimalHearts(animal);
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
                CodeInstruction.Call(
                    typeof(Patches), nameof(Patches.patch_AnimalQueryMenu_new_height)
                ),
            ])
            .Advance(1)
        )
        .InstructionEnumeration();

    static int patch_AnimalQueryMenu_new_height(int height, AnimalQueryMenu menu) => height + (
        Patches.queryMenuOverflowHearts.GetFromBox(menu, () => 0) != 0 ? 28 : 0
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
                $"could not transpile method: does not assert that {typeof(AnimalQueryMenu)}." +
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
                    CodeInstruction.Call(
                        typeof(Patches), nameof(Patches.patch_AnimalQueryMenu_draw_offset)
                    ),
                ])
            )
            .InstructionEnumeration();
    }

    static int patch_AnimalQueryMenu_draw_offset(AnimalQueryMenu menu, int value) => value + (
        Patches.queryMenuOverflowHearts.GetFromBox(menu, () => 0) != 0 ? 28 : 0
    );

    static void postfix_AnimalQueryMenu_draw(AnimalQueryMenu __instance, SpriteBatch b) {
        var hearts = Patches.queryMenuOverflowHearts.GetFromBox(__instance, () => 0);
        if (hearts != 0) {
            var parentOffset = __instance.parentName is null ? 0 : 21;
            Mod.Instance.DrawOverflowHearts(b, hearts, 15, new(
                __instance.xPositionOnScreen + 252,
                __instance.yPositionOnScreen + parentOffset + 288
            ));
        }
    }

    static void postfix_SocialPage_FindSocialCharacters(List<SocialPage.SocialEntry> __result) {
        var mod = Mod.Instance;
        if (mod.Config.NpcOverflowHearts) {
            Utils.SortGroups<SocialPage.SocialEntry, int, BigInteger>(
                list: __result,
                group: entry => !entry.IsPlayer && !entry.IsChild && entry.Character is NPC
                    ? mod.Config.SortByTotalFriendship
                        ? 0
                        : entry.Friendship?.Points ?? 0
                    : null,
                key: entry => entry.Character is NPC npc
                    ? -mod.GetTotalNpcPoints(Game1.player, npc)
                    : 0
            );
        }
    }

    static void postfix_AnimalPage_FindAnimals(List<AnimalPage.AnimalEntry> __result) {
        var mod = Mod.Instance;
        if (mod.Config.AnimalOverflowHearts) {
            Utils.SortGroups<AnimalPage.AnimalEntry, int, BigInteger>(
                list: __result,
                group: entry => entry.Animal is FarmAnimal a
                    ? mod.Config.SortByTotalFriendship
                        ? 0
                        : a.friendshipTowardFarmer.Value
                    : null,
                key: entry => -mod.GetTotalAnimalPoints(entry.Animal)
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
                    $"error patching method {method}: exception {e}",
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
                    $"error patching property getter {type}.{name}: exception {e}",
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
                    $"error patching property setter {type}.{name}: exception {e}",
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
                    $"error patching constructor {constructor}: exception {e}",
                    LogLevel.Error
                );
            }
        }
    }
}
