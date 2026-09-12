using System.Numerics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Mods;
using Netcode;
using StardewModdingAPI;
using HarmonyLib;

namespace HeartsOverflow;

static class Hearts {
    internal static NpcHearts Npc(Mod mod, Farmer player, NPC npc)
        => new(mod, player, npc);

    internal static NpcHeartsByName NpcByName(Mod mod, Farmer player, string npcName)
        => new(mod, player, npcName);

    internal static AnimalHearts Animal(Mod mod, Character animal)
        => new(mod, animal);

    internal static bool NpcIsValid(NPC npc)
        => (npc.CanSocialize || npc is Child) && npc is not Pet;

    internal static bool AnimalIsValid(Character animal)
        => animal is Pet or FarmAnimal;

    static string npcTotalPointsKey(Mod mod, string npcName)
        => $"{mod.ModManifest.UniqueID}_TotalFriendshipPoints[{npcName}]";

    static string animalTotalPointsKey(Mod mod)
        => $"{mod.ModManifest.UniqueID}_TotalFriendshipTowardFarmer";

    internal readonly struct NpcHearts : IObjectHearts {
        internal NpcHearts(Mod mod, Farmer player, NPC npc) {
            this.mod = mod;
            this.player = player;
            this.npc = npc;
            this.valid = mod.Config.NpcOverflowHearts && Hearts.NpcIsValid(npc);
            this.totalKey = Hearts.npcTotalPointsKey(mod, npc.Name);
            this.nonOverflow = player.friendshipData.TryGetValue(npc.Name, out var friendship)
                ? AccessTools.FieldRefAccess<Friendship, NetInt>(friendship, "points") : null;
        }

        readonly Mod? mod;
        readonly Farmer? player;
        readonly NPC? npc;
        readonly bool valid;
        readonly string? totalKey;
        readonly NetInt? nonOverflow;

        int IHearts.PointsPerHeart => NPC.friendshipPointsPerHeartLevel;
        bool IHearts.AllowNegativeOverflow => this.mod?.AllowNegativeOverflow ?? false;

        bool IHearts.ValidForOverflow => this.valid;
        ModDataDictionary? IHearts.TotalPointsDict => this.player?.modData;
        string? IHearts.TotalPointsKey => this.totalKey;
        NetInt? IHearts.NonOverflowField => this.nonOverflow;

        string IHearts.LogPlayer => this.player is Farmer p
            ? $" player {Utils.Posessive(p.Name)}" : "";

        string IHearts.LogTarget => this.npc is NPC n ? $" with NPC {n.Name}" : "";

        void IHearts.Log(string message, LogLevel level) => this.mod?.Monitor.Log(message, level);

        (int, int)? IObjectHearts.GetBounds()
            => this.mod is Mod m && this.player is Farmer p && this.npc is NPC n
                ? (m.NpcMinFriendship(p, n), m.NpcMaxFriendship(p, n)) : null;
    }

    internal readonly struct NpcHeartsByName : IHearts {
        internal NpcHeartsByName(Mod mod, Farmer player, string npcName) {
            this.mod = mod;
            this.player = player;
            this.npcName = npcName;
            this.valid = mod.Config.NpcOverflowHearts;
            this.totalKey = Hearts.npcTotalPointsKey(mod, npcName);
            this.nonOverflow = player.friendshipData.TryGetValue(npcName, out var friendship)
                ? AccessTools.FieldRefAccess<Friendship, NetInt>(friendship, "points") : null;
        }

        readonly Mod? mod;
        readonly Farmer? player;
        readonly string? npcName;
        readonly bool valid;
        readonly string? totalKey;
        readonly NetInt? nonOverflow;

        int IHearts.PointsPerHeart => NPC.friendshipPointsPerHeartLevel;
        bool IHearts.AllowNegativeOverflow => this.mod?.AllowNegativeOverflow ?? false;

        bool IHearts.ValidForOverflow => this.valid;
        ModDataDictionary? IHearts.TotalPointsDict => this.player?.modData;
        string? IHearts.TotalPointsKey => this.totalKey;
        NetInt? IHearts.NonOverflowField => this.nonOverflow;

        string IHearts.LogPlayer => this.player is Farmer p
            ? $" player {Utils.Posessive(p.Name)}" : "";

        string IHearts.LogTarget => this.npcName is string name ? $" with NPC {name}" : "";

        void IHearts.Log(string message, LogLevel level) => this.mod?.Monitor.Log(message, level);
    }

    internal readonly struct AnimalHearts : IObjectHearts {
        internal AnimalHearts(Mod mod, Character animal) {
            this.mod = mod;
            this.animal = animal;
            this.valid =  mod.Config.AnimalOverflowHearts && Hearts.AnimalIsValid(animal);
            this.totalKey = Hearts.animalTotalPointsKey(mod);
            this.nonOverflow = animal is Pet pet
                ? pet.friendshipTowardFarmer
                : animal is FarmAnimal farmAnimal
                    ? farmAnimal.friendshipTowardFarmer : null;
        }

        readonly Mod? mod;
        readonly Character? animal;
        readonly bool valid;
        readonly string? totalKey;
        readonly NetInt? nonOverflow;

        int IHearts.PointsPerHeart => 200;
        bool IHearts.AllowNegativeOverflow => this.mod?.AllowNegativeOverflow ?? false;

        bool IHearts.ValidForOverflow => this.valid;
        ModDataDictionary? IHearts.TotalPointsDict => this.animal?.modData;
        string? IHearts.TotalPointsKey => this.totalKey;
        NetInt? IHearts.NonOverflowField => this.nonOverflow;

        string IHearts.LogPlayer => "";
        string IHearts.LogTarget => this.animal is Character a ? $" with animal {a.Name}" : "";

        void IHearts.Log(string message, LogLevel level) => this.mod?.Monitor.Log(message, level);

        (int, int)? IObjectHearts.GetBounds()
            => this.mod is Mod m && this.animal is Character a
                ? (m.AnimalMinFriendship(a), m.AnimalMaxFriendship(a)) : null;
    }

    internal interface IHearts {
        int PointsPerHeart { get; }
        bool AllowNegativeOverflow { get; }

        bool ValidForOverflow { get; }
        ModDataDictionary? TotalPointsDict { get; }
        string? TotalPointsKey { get; }
        NetInt? NonOverflowField { get; }

        string LogPlayer { get; }
        string LogTarget { get; }

        void Log(string message, LogLevel level);
    }

    internal interface IObjectHearts : IHearts {
        (int, int)? GetBounds();
    }

    extension<T>(T self) where T : struct, IHearts {
        int? nonOverflowPoints {
            get => self.NonOverflowField is not null
                ? Patches.GetNetIntPure(self.NonOverflowField)
                : null;

            set {
                if (self.NonOverflowField is not null && value is int val) {
                    Patches.SetNetIntPure(self.NonOverflowField, val);
                }
            }
        }

        int? nonOverflowHearts => self.nonOverflowPoints is int p ? p / self.PointsPerHeart : null;

        BigInteger? totalPointsEntry {
            get {
                var (dict, key) = (self.TotalPointsDict, self.TotalPointsKey);
                return (dict is null || key is null)
                    ? null : dict.TryGetValue(key, out var data)
                        ? BigInteger.TryParse(data, out var points) ? points : null : null;
            }

            set {
                var (dict, key) = (self.TotalPointsDict, self.TotalPointsKey);
                if (dict is null || key is null) return;

                if (value is BigInteger val) dict[key] = val.ToString();
                else dict.Remove(key);
            }
        }

        BigInteger? totalHeartsEntry => self.totalPointsEntry is BigInteger p
            ? p / self.PointsPerHeart : null;

        internal BigInteger TotalPoints {
            get {
                var nonOverflow = self.nonOverflowPoints;
                var total = nonOverflow is  null || !self.ValidForOverflow
                    ? null : self.totalPointsEntry;

                return total ?? nonOverflow ?? 0;
            }
        }

        internal BigInteger TotalHearts {
            get {
                var nonOverflow = self.nonOverflowHearts;
                var total = nonOverflow is null || !self.ValidForOverflow
                    ? null : self.totalHeartsEntry;

                var hearts = total ?? nonOverflow ?? 0;
                return hearts < 0 && !self.AllowNegativeOverflow ? 0 : hearts;
            }
        }

        internal BigInteger OverflowPoints {
            get {
                if (!(
                    self.ValidForOverflow &&
                    self.nonOverflowPoints is int nonOverflow &&
                    self.totalPointsEntry is BigInteger total
                )) return 0;

                return Hearts.calculateOverflow(total, nonOverflow);
            }
        }

        internal BigInteger OverflowHearts {
            get {
                if (!(
                    self.ValidForOverflow &&
                    self.nonOverflowHearts is int nonOverflow &&
                    self.totalHeartsEntry is BigInteger total
                )) return 0;

                var hearts = Hearts.calculateOverflow(total, nonOverflow);
                return hearts < 0 && !self.AllowNegativeOverflow ? 0 : hearts;
            }
        }

        internal void ClearOverflow() {
            self.totalPointsEntry = null;
            self.Log($"clear{self.LogPlayer} overflow friendship{self.LogTarget}", LogLevel.Trace);
        }
    }

    static BigInteger calculateOverflow(BigInteger total, int nonOverflow)
        => ((total > 0 && total > nonOverflow) || (total < 0 && total < nonOverflow))
            ? total - nonOverflow : 0;

    extension<T>(T self) where T : struct, IObjectHearts {
        internal int ChangePoints(int from, int to, int by) {
            if (!(
                self.ValidForOverflow &&
                (by != 0 || from != to) &&
                self.GetBounds() is var (min, max)
            )) return to;

            string logBy() {
                var s = by == 1 || by == -1 ? "" : "s";
                return $" by {by:+#;-#;0} point{s}";
            }

            if (self.totalPointsEntry is BigInteger total) {
                var orig = total;

                var sum = total + by;
                if (sum >= 0 || sum >= total || self.AllowNegativeOverflow) total = sum;
                else if (total > 0) total = 0;

                to = Utils.Clamp(total, min, max);

                if (total >= min && total <= max) {
                    self.totalPointsEntry = null;
                    self.Log(
                        $"change{self.LogPlayer} friendship{self.LogTarget}{logBy()} to {to} " +
                        $"within bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else if (total != orig) {
                    self.totalPointsEntry = total;
                    self.Log(
                        $"change{self.LogPlayer} total friendship{self.LogTarget}{logBy()} to " +
                        $"{total}",
                        LogLevel.Trace
                    );
                }
            } else {
                total = from + by;
                if ((total < min || total > max) && (total >= 0 || self.AllowNegativeOverflow)) {
                    to = Utils.Clamp(total, min, max);
                    self.totalPointsEntry = total;
                    self.Log(
                        $"change{self.LogPlayer} total friendship{self.LogTarget}{logBy()} to " +
                        $"{total} exceeding bounds {min} to {max}",
                        LogLevel.Trace
                    );
                }
            }

            return to;
        }

        internal void SyncBounds(bool expectChange = true) {
            if (!(
                self.ValidForOverflow &&
                self.nonOverflowPoints is int oldPoints &&
                self.totalPointsEntry is BigInteger total &&
                self.GetBounds() is var (min, max)
            )) return;

            var newPoints = Utils.Clamp(total, min, max);
            if (newPoints != oldPoints) {
                self.nonOverflowPoints = newPoints;
                self.Log(
                    $"sync{self.LogPlayer} friendship{self.LogTarget} from {oldPoints} to " +
                    $"{newPoints} within bounds {min} to {max}",
                    expectChange ? LogLevel.Trace : LogLevel.Warn
                );
            }

            if (total >= min && total <= max) self.totalPointsEntry = null;
        }

        internal void MigrateOverflow(BigInteger overflow) {
            if (!(
                overflow != 0 &&
                self.GetBounds() is var (min, max)
            )) return;

            string logBy() {
                var s = overflow == 1 || overflow == -1 ? "" : "s";
                return $" by {overflow:+#;-#;0} point{s}";
            }

            int friendship;
            if (self.totalPointsEntry is BigInteger total) {
                total += overflow;
                friendship = Utils.Clamp(total, min, max);

                if (total >= min && total <= max) {
                    self.totalPointsEntry = null;
                    self.Log(
                        $"migrate{self.LogPlayer} overflow friendship{self.LogTarget} changing " +
                        $"friendship{logBy()} to {friendship} within bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else {
                    self.totalPointsEntry = total;
                    self.Log(
                        $"migrate{self.LogPlayer} overflow friendship{self.LogTarget} changing " +
                        $"total friendship{logBy()} to {total}",
                        LogLevel.Trace
                    );
                }
            } else {
                total = overflow + (self.nonOverflowPoints ?? 0);
                friendship = Utils.Clamp(total, min, max);

                if (total < min || total > max) {
                    self.totalPointsEntry = total;
                    self.Log(
                        $"migrate{self.LogPlayer} overflow friendship{self.LogTarget} changing " +
                        $"total friendship{logBy()} to {total} exceeding bounds {min} to {max}",
                        LogLevel.Trace
                    );
                } else {
                    self.Log(
                        $"migrate{self.LogPlayer} overflow friendship{self.LogTarget} changing " +
                        $"friendship{logBy()} to {friendship}",
                        LogLevel.Trace
                    );
                }
            }

            self.nonOverflowPoints = friendship;
        }
    }
}
