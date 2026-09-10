using System.Numerics;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Mods;

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

    static int? getNonOverflowNpcPoints(Farmer player, string npcName)
        => player.friendshipData.TryGetValue(npcName, out var friendship)
            ? friendship.Points
            : null;

    static int? getNonOverflowAnimalPoints(Character animal)
        => animal is Pet pet
            ? Patches.GetNetIntPure(pet.friendshipTowardFarmer)
            : animal is FarmAnimal farmAnimal
                ? Patches.GetNetIntPure(farmAnimal.friendshipTowardFarmer)
                : null;

    static void setNonOverflowNpcPoints(Farmer player, string npcName, int points) {
        if (player.friendshipData.TryGetValue(npcName, out var friendship)) {
            friendship.Points = points;
        }
    }

    static void setNonOverflowAnimalPoints(Character animal, int points) {
        if (animal is Pet pet) {
            Patches.SetNetIntPure(pet.friendshipTowardFarmer, points);
        } else if (animal is FarmAnimal farmAnimal) {
            Patches.SetNetIntPure(farmAnimal.friendshipTowardFarmer, points);
        }
    }

    static int? getNonOverflowNpcHearts(Farmer player, string npcName)
        => Mod.nonOverflowNpcPointsToHearts(Mod.getNonOverflowNpcPoints(player, npcName));

    static int? getNonOverflowAnimalHearts(Character animal)
        => Mod.nonOverflowAnimalPointsToHearts(Mod.getNonOverflowAnimalPoints(animal));

    internal static bool NpcIsValid(NPC npc)
        => (npc.CanSocialize || npc is Child) && npc is not Pet;

    internal static bool AnimalIsValid(Character animal)
        => animal is Pet or FarmAnimal;

    bool npcOverflowIsAllowed(NPC npc)
        => this.Config.NpcOverflowHearts && Mod.NpcIsValid(npc);

    bool animalOverflowIsAllowed(Character animal)
        => this.Config.AnimalOverflowHearts && Mod.AnimalIsValid(animal);

    string npcTotalFriendshipModDataKey(string npcName)
        => $"{this.ModManifest.UniqueID}_TotalFriendshipPoints[{npcName}]";

    string animalTotalFriendshipModDataKey()
        => $"{this.ModManifest.UniqueID}_TotalFriendshipTowardFarmer";

    static BigInteger? parsePoints(ModDataDictionary modData, string key)
        => modData.TryGetValue(key, out var data)
            ? BigInteger.TryParse(data, out var points) ? points : null
            : null;

    static void writePoints(ModDataDictionary modData, string key, BigInteger points)
        => modData[key] = points.ToString();

    BigInteger? parseTotalNpcPoints(Farmer player, NPC npc)
        => this.npcOverflowIsAllowed(npc)
            ? Mod.parsePoints(player.modData, this.npcTotalFriendshipModDataKey(npc.Name))
            : null;

    BigInteger? parseTotalNpcPointsByName(Farmer player, string npcName)
        => this.Config.NpcOverflowHearts
            ? Mod.parsePoints(player.modData, this.npcTotalFriendshipModDataKey(npcName))
            : null;

    BigInteger? parseTotalAnimalPoints(Character animal)
        => this.animalOverflowIsAllowed(animal)
            ? Mod.parsePoints(animal.modData, this.animalTotalFriendshipModDataKey())
            : null;

    internal BigInteger GetTotalNpcPoints(Farmer player, NPC npc)
        => this.parseTotalNpcPoints(player, npc) ??
            Mod.getNonOverflowNpcPoints(player, npc.Name) ?? 0;

    internal BigInteger GetTotalNpcPointsByName(Farmer player, string npcName)
        => this.parseTotalNpcPointsByName(player, npcName) ??
            Mod.getNonOverflowNpcPoints(player, npcName) ?? 0;

    internal BigInteger GetTotalAnimalPoints(Character animal)
        => this.parseTotalAnimalPoints(animal) ??
            Mod.getNonOverflowAnimalPoints(animal) ?? 0;

    internal BigInteger GetTotalNpcHearts(Farmer player, NPC npc)
        => this.npcPointsToHearts(this.GetTotalNpcPoints(player, npc));

    internal BigInteger GetTotalNpcHeartsByName(Farmer player, string npcName)
        => this.animalPointsToHearts(this.GetTotalNpcPointsByName(player, npcName));

    internal BigInteger GetTotalAnimalHearts(Character animal)
        => this.animalPointsToHearts(this.GetTotalAnimalPoints(animal));

    internal BigInteger GetOverflowNpcPoints(Farmer player, NPC npc)
        => Mod.calculateOverflow(
            total: this.parseTotalNpcPoints(player, npc),
            nonOverflow: Mod.getNonOverflowNpcPoints(player, npc.Name)
        );

    internal BigInteger GetOverflowNpcPointsByName(Farmer player, string npcName)
        => Mod.calculateOverflow(
            total: this.parseTotalNpcPointsByName(player, npcName),
            nonOverflow: Mod.getNonOverflowNpcPoints(player, npcName)
        );

    internal BigInteger GetOverflowAnimalPoints(Character animal)
        => Mod.calculateOverflow(
            total: this.parseTotalAnimalPoints(animal),
            nonOverflow: Mod.getNonOverflowAnimalPoints(animal)
        );

    internal BigInteger GetOverflowNpcHearts(Farmer player, NPC npc)
        => Mod.calculateOverflow(
            total: this.npcPointsToHearts(this.parseTotalNpcPoints(player, npc)),
            nonOverflow: Mod.getNonOverflowNpcHearts(player, npc.Name)
        );

    internal BigInteger GetOverflowNpcHeartsByName(Farmer player, string npcName)
        => Mod.calculateOverflow(
            total: this.npcPointsToHearts(this.parseTotalNpcPointsByName(player, npcName)),
            nonOverflow: Mod.getNonOverflowNpcHearts(player, npcName)
        );

    internal BigInteger GetOverflowAnimalHearts(Character animal)
        => Mod.calculateOverflow(
            total: this.animalPointsToHearts(this.parseTotalAnimalPoints(animal)),
            nonOverflow: Mod.getNonOverflowAnimalHearts(animal)
        );

    static BigInteger calculateOverflow(BigInteger? total, int? nonOverflow)
        => total is BigInteger t && nonOverflow is int n && ((t > 0 && t > n) || (t < 0 && t < n))
            ? t - n
            : 0;

    BigInteger npcPointsToHearts(BigInteger points)
        => points <= 0 && !this.allowNegativeOverflow()
            ? 0
            : points / NPC.friendshipPointsPerHeartLevel;

    BigInteger? npcPointsToHearts(BigInteger? points)
        => points is BigInteger p
            ? p < 0 && !this.allowNegativeOverflow()
                ? null
                : p / NPC.friendshipPointsPerHeartLevel
            : null;

    static int? nonOverflowNpcPointsToHearts(int? points)
        => points is int p ? p / NPC.friendshipPointsPerHeartLevel : null;

    BigInteger animalPointsToHearts(BigInteger points)
        => points <= 0 && !this.allowNegativeOverflow()
            ? 0
            : points / 200;

    BigInteger? animalPointsToHearts(BigInteger? points)
        => points is BigInteger p
            ? p < 0 && !this.allowNegativeOverflow()
                ? null
                : p / 200
            : null;

    static int? nonOverflowAnimalPointsToHearts(int? points)
        => points is int p ? p / 200 : null;

    internal void ClearNpcOverflow(Farmer player, NPC npc)
        => this.ClearNpcOverflowByName(player, npc.Name);

    internal void ClearNpcOverflowByName(Farmer player, string npcName) {
        player.modData.Remove(this.npcTotalFriendshipModDataKey(npcName));
        this.Monitor.Log(
            $"clear player {Utils.Posessive(player.Name)} overflow friendship with NPC {npcName}",
            LogLevel.Trace
        );
    }

    internal void ClearAnimalOverflow(Character animal) {
        animal.modData.Remove(this.animalTotalFriendshipModDataKey());
        this.Monitor.Log(
            $"clear overflow friendship with animal {animal.Name}",
            LogLevel.Trace
        );
    }

    internal int NpcMinFriendship(Farmer player, NPC npc) => 0;
    internal int NpcMaxFriendship(Farmer player, NPC npc)
        => (Patches.GetNpcMaxHearts(player, npc) + 1) * NPC.friendshipPointsPerHeartLevel - 1;

    internal int AnimalMinFriendship(Character animal) => 0;
    internal int AnimalMaxFriendship(Character animal) => 1000;

    bool allowNegativeOverflow() => false;

    internal int ChangeNpcPoints(Farmer player, NPC npc, int from, int to, int by) {
        if (this.npcOverflowIsAllowed(npc) && !(by == 0 && from == to)) {
            var min = this.NpcMinFriendship(player, npc);
            var max = this.NpcMaxFriendship(player, npc);

            var key = this.npcTotalFriendshipModDataKey(npc.Name);
            if (Mod.parsePoints(player.modData, key) is BigInteger total) {
                var orig = total;

                var sum = total + by;
                if (sum >= 0 || sum >= total || this.allowNegativeOverflow()) total = sum;
                else if (total > 0) total = 0;

                to = Utils.Clamp(total, min, max);

                if (total >= min && total <= max) {
                    player.modData.Remove(key);
                    this.Monitor.Log(
                        $"change player {Utils.Posessive(player.Name)} friendship with NPC " +
                        $"{npc.Name} by {by:+#;-#;0} points to {to} within bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else if (total != orig) {
                    Mod.writePoints(player.modData, key, total);
                    this.Monitor.Log(
                        $"change player {Utils.Posessive(player.Name)} total friendship with NPC " +
                        $"{npc.Name} by {by:+#;-#;0} points to {total}",
                        LogLevel.Trace
                    );
                }
            } else {
                total = from + by;
                if ((total < min || total > max) && (total >= 0 || this.allowNegativeOverflow())) {
                    to = Utils.Clamp(total, min, max);
                    Mod.writePoints(player.modData, key, total);
                    this.Monitor.Log(
                        $"change player {Utils.Posessive(player.Name)} total friendship with NPC " +
                        $"{npc.Name} by {by:+#;-#;0} points to {total} exceeding bounds {min} to " +
                        $"{max}",
                        LogLevel.Trace
                    );
                }
            }
        }

        return to;
    }

    internal int ChangeAnimalPoints(Character animal, int from, int to, int by) {
        if (this.animalOverflowIsAllowed(animal) && !(by == 0 && from == to)) {
            var min = this.AnimalMinFriendship(animal);
            var max = this.AnimalMaxFriendship(animal);

            var key = this.animalTotalFriendshipModDataKey();
            if (Mod.parsePoints(animal.modData, key) is BigInteger total) {
                var orig = total;

                var sum = total + by;
                if (sum >= 0 || sum >= total || this.allowNegativeOverflow()) total = sum;
                else if (total > 0) total = 0;

                to = Utils.Clamp(total, min, max);

                if (total >= min && total <= max) {
                    animal.modData.Remove(key);
                    this.Monitor.Log(
                        $"change friendship with animal {animal.Name} by {by:+#;-#;0} points to " +
                        $"{to} within bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else if (total != orig) {
                    Mod.writePoints(animal.modData, key, total);
                    this.Monitor.Log(
                        $"change total friendship with animal {animal.Name} by {by:+#;-#;0} " +
                        $"points to {total}",
                        LogLevel.Trace
                    );
                }
            } else {
                total = from + by;
                if ((total < min || total > max) && (total >= 0 || this.allowNegativeOverflow())) {
                    to = Utils.Clamp(total, min, max);
                    Mod.writePoints(animal.modData, key, total);
                    this.Monitor.Log(
                        $"change total friendship with animal {animal.Name} by {by:+#;-#;0} " +
                        $"points to {total} exceeding bounds {min} to {max}",
                        LogLevel.Trace
                    );
                }
            }
        }

        return to;
    }

    void syncAllFriendship() {
        if (Context.IsWorldReady) {
            Utility.ForEachCharacter(npc => {
                this.SyncNpcFriendshipBounds(Game1.player, npc, expectChange: false);
                return true;
            });

            if (Context.IsMainPlayer) {
                Utility.ForEachCharacter(animal => {
                    this.SyncAnimalFriendshipBounds(animal, expectChange: false);
                    return true;
                });

                Utility.ForEachLocation(
                    location => {
                        foreach (var animal in location.Animals.Values) {
                            // This can produe harmless WARNs in multiplayer when a client causes an
                            // animal's friendship to exceed its bounds.
                            this.SyncAnimalFriendshipBounds(animal, expectChange: false);
                        }
                        return true;
                    },
                    includeGenerated: true
                );
            }
        }
    }

    internal void SyncNpcFriendshipBounds(Farmer player, NPC npc, bool expectChange = true) {
        if (
            this.npcOverflowIsAllowed(npc) &&
            Mod.getNonOverflowNpcPoints(player, npc.Name) is int oldPoints
        ) {
            var key = this.npcTotalFriendshipModDataKey(npc.Name);
            if (Mod.parsePoints(player.modData, key) is BigInteger total) {
                var min = this.NpcMinFriendship(player, npc);
                var max = this.NpcMaxFriendship(player, npc);

                var newPoints = Utils.Clamp(total, min, max);
                if (oldPoints != newPoints) {
                    this.Monitor.Log(
                        $"sync player {Utils.Posessive(player.Name)} friendship with NPC " +
                        $"{npc.Name} from {oldPoints} to {newPoints} within bounds {min} to {max}",
                        expectChange ? LogLevel.Trace : LogLevel.Warn
                    );

                    Mod.setNonOverflowNpcPoints(player, npc.Name, newPoints);
                }

                if (total >= min && total <= max) player.modData.Remove(key);
            }
        }
    }

    internal void SyncAnimalFriendshipBounds(Character animal, bool expectChange = true) {
        if (
            this.animalOverflowIsAllowed(animal) &&
            Mod.getNonOverflowAnimalPoints(animal) is int oldPoints
        ) {
            var key = this.animalTotalFriendshipModDataKey();
            if (Mod.parsePoints(animal.modData, key) is BigInteger total) {
                var min = this.AnimalMinFriendship(animal);
                var max = this.AnimalMaxFriendship(animal);

                var newPoints = Utils.Clamp(total, min, max);
                if (oldPoints != newPoints) {
                    this.Monitor.Log(
                        $"sync friendship with animal {animal.Name} from {oldPoints} to " +
                        $"{newPoints} within bounds {min} to {max}",
                        expectChange ? LogLevel.Trace : LogLevel.Warn
                    );

                    Mod.setNonOverflowAnimalPoints(animal, newPoints);
                }

                if (total >= min && total <= max) animal.modData.Remove(key);
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
                            this.migrateOverflowNpcPoints(player, npc, overflow);
                        }
                    }
                } finally {
                    foreach (var k in hits) npc.modData.Remove(k);
                }

                return true;
            });
        }
    }

    void migrateOverflowNpcPoints(Farmer player, NPC npc, BigInteger overflow) {
        if (overflow != 0) {
            var min = this.NpcMinFriendship(player, npc);
            var max = this.NpcMaxFriendship(player, npc);

            int friendship;
            var key = this.npcTotalFriendshipModDataKey(npc.Name);
            if (Mod.parsePoints(player.modData, key) is BigInteger total) {
                total += overflow;
                friendship = Utils.Clamp(total, min, max);

                if (total >= min && total <= max) {
                    player.modData.Remove(key);
                    this.Monitor.Log(
                        $"migrate player {Utils.Posessive(player.Name)} overflow friendship with " +
                        $"NPC {npc.Name} changing friendship by {overflow:+#;-#;0} points to " +
                        $"{friendship} within bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else {
                    Mod.writePoints(player.modData, key, total);
                    this.Monitor.Log(
                        $"migrate player {Utils.Posessive(player.Name)} overflow friendship with " +
                        $"NPC {npc.Name} changing total friendship by {overflow:+#;-#;0} points " +
                        $"to {total}",
                        LogLevel.Trace
                    );
                }
            } else {
                total = overflow + (Mod.getNonOverflowNpcPoints(player, npc.Name) ?? 0);
                friendship = Utils.Clamp(total, min, max);

                if (total < min || total > max) {
                    Mod.writePoints(player.modData, key, total);
                    this.Monitor.Log(
                        $"migrate player {Utils.Posessive(player.Name)} overflow friendship with " +
                        $"NPC {npc.Name} changing total friendship by {overflow:+#;-#;0} points " +
                        $"to {total} exceeding bounds {min} to {max}",
                        LogLevel.Trace
                    );
                }
            }

            Mod.setNonOverflowNpcPoints(player, npc.Name, friendship);
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
