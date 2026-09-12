using System.Numerics;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace HeartsOverflow;

sealed class Mod : StardewModdingAPI.Mod {
    public override void Entry(IModHelper helper) {
        Mod.instance = this;
        this.Config = Config.Read(this);
        this.font = Texture2D.FromStream(Game1.graphics.GraphicsDevice, Mod.Asset("font.png"));

        helper.Events.GameLoop.GameLaunched += (_, _) => this.onGameLaunched();
        helper.Events.GameLoop.UpdateTicking += (_, _) => this.onUpdateTicking();
        helper.Events.GameLoop.SaveLoaded += (_, _) => this.onSaveLoaded();
        helper.Events.GameLoop.Saving += (_, _) => this.onSaving();

        Patches.Apply(this);
    }

    public override object GetApi() => new ApiImpl(this);

    static Mod? instance;
    internal static Mod Instance => Mod.instance ?? throw new NullReferenceException(
        $"tried to access {typeof(Mod)} before initialization"
    );

    internal Config Config { get; set; } = new();
    Texture2D? font;

    void onGameLaunched() {
        new Extensibility(this).Register();
        Config.Register(this);
    }

    void onUpdateTicking() {
        Patches.ClearThreadState();
        this.syncAllFriendship();
    }

    void onSaveLoaded() {
        this.compatMigrateData();
    }

    void onSaving() {
        this.syncAllFriendship();
    }

    internal void WithApi<T>(string id, Action<T> action) where T : class {
        try {
            var api = this.Helper.ModRegistry.GetApi<T>(id);
            if (api is not null) action(api);
        } catch (Exception e) {
            this.Monitor.Log($"error calling API for mod {id}: exception {e}", LogLevel.Error);
        }
    }

    static Stream? Asset(string assetFile) => typeof(Mod).Assembly
        .GetManifestResourceStream($"{nameof(HeartsOverflow)}.assets.{assetFile}");

    internal int NpcMinFriendship(Farmer player, NPC npc) => 0;
    internal int NpcMaxFriendship(Farmer player, NPC npc)
        => (Patches.GetNpcMaxHearts(player, npc) + 1) * NPC.friendshipPointsPerHeartLevel - 1;

    internal int AnimalMinFriendship(Character animal) => 0;
    internal int AnimalMaxFriendship(Character animal) => 1000;

    internal bool AllowNegativeOverflow => false;

    void syncAllFriendship() {
        if (Context.IsWorldReady) {
            Utility.ForEachCharacter(npc => {
                Hearts.Npc(this, Game1.player, npc).SyncBounds(expectChange: false);
                return true;
            });

            if (Context.IsMainPlayer) {
                Utility.ForEachCharacter(animal => {
                    Hearts.Animal(this, animal).SyncBounds(expectChange: false);
                    return true;
                });

                Utility.ForEachLocation(
                    location => {
                        foreach (var animal in location.Animals.Values) {
                            // This can produe harmless WARNs in multiplayer when a client causes an
                            // animal's friendship to exceed its bounds.
                            Hearts.Animal(this, animal).SyncBounds(expectChange: false);
                        }
                        return true;
                    },
                    includeGenerated: true
                );
            }
        }
    }

    void compatMigrateData() {
        if (Context.IsMainPlayer) {
            var players = Game1.getAllFarmers().ToDictionary(p => p.UniqueMultiplayerID);
            var keyStart = $"{this.ModManifest.UniqueID}.OverflowFriendshipPoints[";
            var keyEnd = "]";

            Utility.ForEachCharacter(npc => {
                var hits = new List<string>();
                try {
                    foreach (var (k, v) in npc.modData.Pairs) {
                        if (
                            k.StartsWith(keyStart) &&
                            k.EndsWith(keyEnd) &&
                            long.TryParse(k[keyStart.Length..^keyEnd.Length], out var id) &&
                            players.TryGetValue(id, out var player) &&
                            BigInteger.TryParse(v, out var overflow)
                        ) {
                            hits.Add(k);
                            Hearts.Npc(this, player, npc).MigrateOverflow(overflow);
                        }
                    }
                } finally {
                    foreach (var k in hits) npc.modData.Remove(k);
                }

                return true;
            });
        }
    }

    internal void DrawOverflowHearts(SpriteBatch b, BigInteger hearts, int width, Vector2 at) {
        var text = $"{hearts:+#;-#;0}×";
        if (text.Length > width) {
            var sign = hearts > 0 ? "+" : hearts < 0 ? "-" : "";

            if (LocalizedContentManager.CurrentLanguageCode ==
                LocalizedContentManager.LanguageCode.en
            ) {
                var n = Utils.Max(1, width - (3 + sign.Length));
                text = $"{sign}M{new('A', n)}X×";
            } else {
                var n = Utils.Max(1, width - (1 + sign.Length));
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
                >= '0' and <= '9' => c - '0',
                '+' => 10, '-' => 11, '×' => 12,
                '▒' => 14, 'M' => 15, 'A' => 16, 'X' => 17,
                _ => 13,
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
}
