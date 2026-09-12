using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace HeartsOverflow;

static class Utils {
    internal static int ToIntSaturating(BigInteger n)
        => Utils.Clamp(n, int.MinValue, int.MaxValue);

    internal static int Clamp(BigInteger n, int min, int max)
        => n < min ? min : n > max ? max : (int)n;

    internal static int Min(int a, int b) => a > b ? b : a;
    internal static int Max(int a, int b) => a < b ? b : a;

    internal static string Posessive(string name) {
        var posessive = name.EndsWith('s') ? "'" : "'s";
        return $"{name}{posessive}";
    }

    extension<T, U>(ConditionalWeakTable<T, Utils.Box<U>> cwt) where T : class {
        internal U GetFromBox(T key, Func<U> fallback)
            => cwt.TryGetValue(key, out var box) ? box.Value : fallback();
    }

    internal static void SortGroups<T, G, K>(IList<T> list, Func<T, G?> group, Func<T, K> key)
    where G : struct, IEquatable<G> where K : IComparable<K> {
        int? sort = null;
        G? prev = null;
        for (int i = 0; i < list.Count; i++) {
            var curr = group(list[i]);

            if (curr is not null && curr.Equals(prev)) sort ??= i - 1;
            else if (sort is not null) {
                Utils.SortStable(list, (int)sort, i, key);
                sort = null;
            }

            prev = curr;
        }

        if (sort is not null) Utils.SortStable(list, (int)sort, list.Count, key);
    }

    internal static void SortStable<T, K>(IList<T> list, int start, int end, Func<T, K> key)
    where K : IComparable<K> {
        for (int i = start + 1; i < end; i++) {
            var v = list[i];
            var k = key(v);

            int j;
            for (j = i - 1; j >= start; j--) {
                if (k.CompareTo(key(list[j])) >= 0) break;
                list[j + 1] = list[j];
            }

            list[j + 1] = v;
        }
    }

    internal static class OpCodeSets {
        internal static readonly OpCode[] STLOC = [
            OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3, OpCodes.Stloc_S,
            OpCodes.Stloc,
        ];

        internal static readonly OpCode[] LCD_I4 = [
            OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7,
            OpCodes.Ldc_I4_8, OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_S, OpCodes.Ldc_I4,
        ];
    }

    internal static CodeInstruction StlocToLdloc(this CodeInstruction instr)
        => instr.opcode switch {
            var op when op == OpCodes.Stloc_0 => new(OpCodes.Ldloc_0),
            var op when op == OpCodes.Stloc_1 => new(OpCodes.Ldloc_1),
            var op when op == OpCodes.Stloc_2 => new(OpCodes.Ldloc_2),
            var op when op == OpCodes.Stloc_3 => new(OpCodes.Ldloc_3),
            var op when op == OpCodes.Stloc_S => new(OpCodes.Ldloc_S, instr.operand),
            var op when op == OpCodes.Stloc => new(OpCodes.Ldloc, instr.operand),
            _ => throw new ArgumentException("invalid opcode", nameof(instr)),
        };

    internal sealed class Box<T> {
        internal Box(T value) => this.Value = value;
        internal T Value;
    }
}
