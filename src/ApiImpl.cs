using System.Numerics;
using StardewValley;

namespace HeartsOverflow;

public sealed class ApiImpl : IHeartsOverflowApi {
    internal ApiImpl(Mod mod) { this.mod = mod; }
    Mod mod;

    public BigInteger GetNpcTotalHearts(Farmer player, NPC npc)
        => player is not null && npc is not null
            ? Hearts.Npc(this.mod, player, npc).TotalHearts : 0;

    public BigInteger GetNpcTotalFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null
            ? Hearts.Npc(this.mod, player, npc).TotalPoints : 0;

    public BigInteger GetNpcOverflowHearts(Farmer player, NPC npc)
        => player is not null && npc is not null
            ? Hearts.Npc(this.mod, player, npc).OverflowHearts : 0;

    public BigInteger GetNpcOverflowFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null
            ? Hearts.Npc(this.mod, player, npc).OverflowPoints : 0;

    public void ClearNpcOverflowFriendship(Farmer player, NPC npc) {
        if (player is not null && npc is not null) {
            Hearts.Npc(this.mod, player, npc).ClearOverflow();
        }
    }

    public BigInteger GetAnimalTotalHearts(Character animal)
        => animal is not null ? Hearts.Animal(this.mod, animal).TotalHearts : 0;

    public BigInteger GetAnimalTotalFriendshipPoints(Character animal)
        => animal is not null ? Hearts.Animal(this.mod, animal).TotalPoints : 0;

    public BigInteger GetAnimalOverflowHearts(Character animal)
        => animal is not null ? Hearts.Animal(this.mod, animal).OverflowHearts : 0;

    public BigInteger GetAnimalOverflowFriendshipPoints(Character animal)
        => animal is not null ? Hearts.Animal(this.mod, animal).OverflowPoints : 0;

    public void ClearAnimalOverflowFriendship(Character animal) {
        if (animal is not null) Hearts.Animal(this.mod, animal).ClearOverflow();
    }
}
