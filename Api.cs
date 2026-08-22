// This is free and unencumbered software released into the public domain.
//
// Anyone is free to copy, modify, publish, use, compile, sell, or distribute this software, either
// in source code form or as a compiled binary, for any purpose, commercial or non-commercial, and
// by any means.
//
// In jurisdictions that recognize copyright laws, the author or authors of this software dedicate
// any and all copyright interest in the software to the public domain. We make this dedication for
// the benefit of the public at large and to the detriment of our heirs and successors. We intend
// this dedication to be an overt act of relinquishment in perpetuity of all present and future
// rights to this software under copyright law.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
// NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Numerics;
using StardewValley;

// To use this API, first add `Esper89.HeartsOverflow` as a dependency in your `manifest.json`. Copy
// this file into your mod, then remove any methods you won't use for compatibility. Then call
// `helper.ModRegistry.GetApi<HeartsOverflow.IHeartsOverflowApi>("Esper89.HeartsOverflow")` to get
// an instance of the API object. Make sure to check that the object isn't `null` before using it.

namespace HeartsOverflow;

/// <summary>Hearts Overflow's public API for other C# mods to use.</summary>
public interface IHeartsOverflowApi {
    /// <summary>Get a player's total hearts with an NPC, including overflow hearts.</summary>
    /// <param name="player">The player who has the heart value to get.</param>
    /// <param name="npc">The NPC who the heart value is with.</param>
    /// <returns>The total number of hearts the player has with the NPC.</returns>
    BigInteger GetNpcTotalHearts(Farmer player, NPC npc);

    /// <summary>Get a player's total friendship points with an NPC, including overflow.</summary>
    /// <param name="player">The player who has the friendship value to get.</param>
    /// <param name="npc">The NPC who the friendship value is with.</param>
    /// <returns>The total number of friendship points the player has with the NPC.</returns>
    BigInteger GetNpcTotalFriendshipPoints(Farmer player, NPC npc);

    /// <summary>Get a player's overflow hearts with an NPC.</summary>
    /// <param name="player">The player who has the overflow hearts to get.</param>
    /// <param name="npc">The NPC who the overflow hearts are with.</param>
    /// <returns>The number of overflow hearts the player has with the NPC.</returns>
    BigInteger GetNpcOverflowHearts(Farmer player, NPC npc);

    /// <summary>Get a player's overflow friendship points with an NPC.</summary>
    /// <param name="player">The player who has the overflow friendship points to get.</param>
    /// <param name="npc">The NPC who the overflow friendship points are with.</param>
    /// <returns>The number of overflow friendship points the player has with the NPC.</returns>
    /// <remarks>This value does not map cleanly to hearts.</remarks>
    BigInteger GetNpcOverflowFriendshipPoints(Farmer player, NPC npc);

    /// <summary>Clear a player's overflow friendship with an NPC.</summary>
    /// <param name="player">The player who has the overflow friendship to clear.</param>
    /// <param name="npc">The NPC who the overflow friendship is with.</param>
    /// <remarks>Works even if the NPC is not currently valid for overflow friendship.</remarks>
    void ClearNpcOverflowFriendship(Farmer player, NPC npc);

    /// <summary>Get a pet or farm animal's total hearts, including overflow hearts.</summary>
    /// <param name="animal">The animal who the heart value is with.</param>
    /// <returns>The total number of hearts the animal has.</returns>
    BigInteger GetAnimalTotalHearts(Character animal);

    /// <summary>Get a pet or farm animal's total friendship points, including overflow.</summary>
    /// <param name="animal">The animal who the friendship value is with.</param>
    /// <returns>The total number of friendship points the animal has.</returns>
    BigInteger GetAnimalTotalFriendshipPoints(Character animal);

    /// <summary>Get a pet or farm animal's overflow hearts.</summary>
    /// <param name="animal">The animal who the overflow hearts are with.</param>
    /// <returns>The number of overflow hearts the animal has.</returns>
    BigInteger GetAnimalOverflowHearts(Character animal);

    /// <summary>Get a pet or farm animal's overflow friendship points.</summary>
    /// <param name="animal">The animal who the overflow friendship points are with.</param>
    /// <returns>The number of overflow friendship points the animal has.</returns>
    BigInteger GetAnimalOverflowFriendshipPoints(Character animal);

    /// <summary>Clear an animal's overflow friendship.</summary>
    /// <param name="animal">The animal who the overflow friendship is with.</param>
    /// <remarks>Works even if the animal is not currently valid for overflow friendship.</remarks>
    void ClearAnimalOverflowFriendship(Character animal);
}
