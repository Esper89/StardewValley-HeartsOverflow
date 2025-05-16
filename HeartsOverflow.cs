using System.Numerics;
using System.Reflection.Emit;
using StardewModdingAPI;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

internal sealed class Mod : StardewModdingAPI.Mod
{
    public override void Entry(IModHelper helper)
    {
        Mod.instance = this;
        this.font = helper.ModContent.Load<Texture2D>("assets/font.png");

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(Farmer), nameof(Farmer.changeFriendship)),
            transpiler: new HarmonyMethod(
                typeof(Mod), nameof(Mod.transpile_Farmer_changeFriendship)
            )
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(SocialPage), nameof(SocialPage.drawNPCSlot)),
            postfix: new HarmonyMethod(typeof(Mod), nameof(Mod.patch_SocialPage_drawNPCSlot))
        );
    }

    private static Mod? instance;
    private Texture2D? font;

    private string modDataKey(Farmer player)
        => $"{this.ModManifest.UniqueID}.OverflowFriendshipPoints[{player.UniqueMultiplayerID}]";

    private BigInteger getPoints(Farmer player, Character npc)
        => npc.modData.TryGetValue(this.modDataKey(player), out string data)
            ? BigInteger.TryParse(data, out BigInteger points) ? points : BigInteger.Zero
            : BigInteger.Zero;

    private void addPoints(Farmer player, NPC npc, int points)
        => npc.modData[this.modDataKey(player)] = (this.getPoints(player, npc) + points).ToString();

    private BigInteger getHearts(Farmer player, Character npc)
    {
        var points = this.getPoints(player, npc);
        points += 249;

        if (points < 0 && points % 250 != 0) return points / 250 - 1;
        else return points / 250;
    }

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
        if (overflow > 0 && amount > 0) Mod.instance!.addPoints(player, npc, overflow);
        return Math.Min(total, max);
    }

    private static void patch_SocialPage_drawNPCSlot(SocialPage __instance, SpriteBatch b, int i)
    {
        var hearts = Mod.instance!.getHearts(Game1.player, __instance.GetSocialEntry(i).Character);
        if (hearts != 0) Mod.drawHearts(b, hearts, 24, new(
            __instance.xPositionOnScreen + 632,
            __instance.sprites[i].bounds.Y + 8
        ));
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
}
