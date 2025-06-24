using System.Numerics;
using System.Reflection.Emit;
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
        this.font = Texture2D.FromStream(Game1.graphics.GraphicsDevice, Mod.Asset("font.png"));

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(Farmer), nameof(Farmer.changeFriendship)),
            transpiler: new HarmonyMethod(
                typeof(Mod), nameof(Mod.transpile_Farmer_changeFriendship)
            )
        );
        harmony.Patch(
            original: AccessTools.Method(
                typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Get)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_NetFieldBase_int_NetInt_Get)
            )
        );
        harmony.Patch(
            original: AccessTools.PropertyGetter(
                typeof(NetFieldBase<int, NetInt>), nameof(NetFieldBase<int, NetInt>.Value)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_NetFieldBase_int_NetInt_Value_get)
            )
        );
        harmony.Patch(
            original: AccessTools.Method(
                typeof(Math), nameof(Math.Min), [typeof(int), typeof(int)]
            ),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_Math_Min_int_int))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(FarmAnimal), "initNetFields"),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_FarmAnimal_initNetFields))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Pet), "initNetFields"),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_Pet_initNetFields))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(SocialPage), nameof(SocialPage.drawNPCSlot)),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.postfix_SocialPage_drawNPCSlot))
        );
        harmony.Patch(
            original: AccessTools.Method(
                typeof(ProfileMenu), "drawNPCSlotHeart"
            ),
            prefix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.prefix_ProfileMenu_drawNPCSlotHeart)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_ProfileMenu_drawNPCSlotHeart)
            )
        );
        harmony.Patch(
            original: AccessTools.Method(
                typeof(SocialPage), nameof(SocialPage.FindSocialCharacters)
            ),
            postfix: new HarmonyMethod(
                typeof(Mod), nameof(Mod.postfix_SocialPage_FindSocialCharacters)
            )
        );
    }

    private static Stream? Asset(string assetFile) => typeof(Mod).Assembly
        .GetManifestResourceStream($"{nameof(HeartsOverflow)}.assets.{assetFile}");

    private static Mod? instance;
    private Texture2D? font;

    private static string modDataKey() => $"Esper89.HeartsOverflow.OverflowFriendshipPoints";

    private static string modDataKey(Farmer player)
        => $"{Mod.modDataKey()}[{player.UniqueMultiplayerID}]";

    private static BigInteger parsePoints(Character c, string key)
        => c.modData.TryGetValue(key, out string data)
            ? BigInteger.TryParse(data, out BigInteger points) ? points : BigInteger.Zero
            : BigInteger.Zero;

    private static BigInteger getPoints(Character c) => Mod.parsePoints(c, Mod.modDataKey());

    private static BigInteger getPoints(Character c, Farmer player)
        => Mod.parsePoints(c, Mod.modDataKey(player));

    private static void addPoints(Character c, int points)
    {
        var key = modDataKey();
        c.modData[key] = (Mod.parsePoints(c, key) + points).ToString();
    }

    private static void addPoints(Character c, Farmer player, int points)
    {
        var key = modDataKey(player);
        c.modData[key] = (Mod.parsePoints(c, key) + points).ToString();
    }

    private static BigInteger hearts(BigInteger points)
    {
        points += 249;
        if (points < 0 && points % 250 != 0) return points / 250 - 1;
        else return points / 250;
    }

    private static BigInteger getHearts(Character c) => Mod.hearts(getPoints(c));

    private static BigInteger getHearts(Character c, Farmer player)
        => Mod.hearts(getPoints(c, player));

    private static IEnumerable<CodeInstruction> transpile_Farmer_changeFriendship(
        IEnumerable<CodeInstruction> instructions
    ) => new CodeMatcher(instructions)
        .MatchStartForward([
            new(OpCodes.Call, AccessTools.Method(
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

    private static NetInt? lastNetIntAccessed = null;
    private static int? lastMinWith1000 = null;

    private static void postfix_NetFieldBase_int_NetInt_Get(NetFieldBase<int, NetInt> __instance)
    {
        if (__instance is NetInt netInt)
        {
            Mod.lastNetIntAccessed = netInt;
            Mod.lastMinWith1000 = null;
        }
    }

    private static void postfix_NetFieldBase_int_NetInt_Value_get(
        NetFieldBase<int, NetInt> __instance
    ) => Mod.postfix_NetFieldBase_int_NetInt_Get(__instance);

    private static void postfix_Math_Min_int_int(int val1, int val2)
    {
        if (Mod.lastNetIntAccessed is not null)
        {
            if (val1 == 1000) Mod.lastMinWith1000 = val2;
            else if (val2 == 1000) Mod.lastMinWith1000 = val1;
        }
    }

    private static void postfix_FarmAnimal_initNetFields(FarmAnimal __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent +=
            (on, from, to) => Mod.animalFriendshipChanged(__instance, on, from, to);

    private static void postfix_Pet_initNetFields(Pet __instance)
        => __instance.friendshipTowardFarmer.fieldChangeEvent +=
            (on, from, to) => Mod.animalFriendshipChanged(__instance, on, from, to);

    private static void animalFriendshipChanged(Character animal, NetInt on, int from, int to)
    {
        if (NetInt.ReferenceEquals(Mod.lastNetIntAccessed, on) && Mod.lastMinWith1000 is int last)
        {
            Mod.lastNetIntAccessed = null;
            Mod.lastMinWith1000 = null;
            if (to >= from && last > 1000 && to == 1000) Mod.addPoints(animal, last - 1000);
        }
    }

    private static void postfix_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i)
    {
        var hearts = Mod.getHearts(__instance.GetSocialEntry(i).Character, Game1.player);
        if (hearts != 0) Mod.drawHearts(b, hearts, 24, new(
            __instance.xPositionOnScreen + 632,
            __instance.sprites[i].bounds.Y + 8
        ));
    }

    private static void prefix_ProfileMenu_drawNPCSlotHeart(
        ref float heartDrawStartY,
        SocialPage.SocialEntry entry
    )
    {
        var overflowHearts = Mod.getHearts(entry.Character, Game1.player);
        if (overflowHearts != 0 && heartDrawStartY >= 0) heartDrawStartY += 16;
    }

    private static void postfix_ProfileMenu_drawNPCSlotHeart(
        ProfileMenu __instance, SpriteBatch b,
        float heartDrawStartX, float heartDrawStartY,
        SocialPage.SocialEntry entry,
        int hearts
    )
    {
        var overflowHearts = Mod.getHearts(entry.Character, Game1.player);
        if (hearts == 0 && overflowHearts != 0)
        {
            var heartDisplayPosition = AccessTools.FieldRefAccess<ProfileMenu, Vector2>(
                __instance, "_heartDisplayPosition"
            );

            var below = heartDrawStartY < 0;
            Mod.drawHearts(b, overflowHearts, below ? 13 : 26, new(
                heartDrawStartX + 316,
                heartDisplayPosition.Y + heartDrawStartY + (below ? 32 : -32)
            ));
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
                color: Game1.textColor,
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
        Utils.SortGroups<SocialPage.SocialEntry, int, BigInteger>(
            __result,
            entry => !entry.IsPlayer && !entry.IsChild ? entry.Friendship?.Points ?? 0 : null,
            entry => -Mod.getPoints(entry.Character, Game1.player)
        );
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
}
