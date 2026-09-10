using System.Numerics;
using StardewValley;

namespace HeartsOverflow;

public sealed class ApiImpl : IHeartsOverflowApi {
    internal ApiImpl(Mod mod) { this.mod = mod; }
    Mod mod;

    public BigInteger GetNpcTotalHearts(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetTotalNpcHearts(player, npc) : 0;

    public BigInteger GetNpcTotalFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetTotalNpcPoints(player, npc) : 0;

    public BigInteger GetNpcOverflowHearts(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetOverflowNpcHearts(player, npc) : 0;

    public BigInteger GetNpcOverflowFriendshipPoints(Farmer player, NPC npc)
        => player is not null && npc is not null ? this.mod.GetOverflowNpcPoints(player, npc) : 0;

    public void ClearNpcOverflowFriendship(Farmer player, NPC npc) {
        if (player is not null && npc is not null) this.mod.ClearNpcOverflow(player, npc);
    }

    public BigInteger GetAnimalTotalHearts(Character animal)
        => animal is not null ? this.mod.GetTotalAnimalHearts(animal) : 0;

    public BigInteger GetAnimalTotalFriendshipPoints(Character animal)
        => animal is not null ? this.mod.GetTotalAnimalPoints(animal) : 0;

    public BigInteger GetAnimalOverflowHearts(Character animal)
        => animal is not null ? this.mod.GetOverflowAnimalHearts(animal) : 0;

    public BigInteger GetAnimalOverflowFriendshipPoints(Character animal)
        => animal is not null ? this.mod.GetOverflowAnimalPoints(animal) : 0;

    public void ClearAnimalOverflowFriendship(Character animal) {
        if (animal is not null) this.mod.ClearAnimalOverflow(animal);
    }
}
