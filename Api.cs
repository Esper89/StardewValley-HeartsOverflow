using System.Numerics;
using StardewValley;

namespace HeartsOverflow;

public interface IHeartsOverflowApi {
    BigInteger GetNpcOverflowHearts(NPC npc, Farmer player);
    BigInteger GetNpcOverflowFriendshipPoints(NPC npc, Farmer player);
    void ClearNpcOverflowFriendship(NPC npc, Farmer player);

    BigInteger GetAnimalOverflowHearts(Character animal);
    BigInteger GetAnimalOverflowFriendshipPoints(Character animal);
    void ClearAnimalOverflowFriendship(Character animal);
}
