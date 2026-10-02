using System;
using System.Collections.Generic;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Part matching, kept free of Unity and game types on purpose so that the very same source file
    /// can be compiled and executed outside the game (see _reference/MatcherTest.cs).
    ///
    /// Rules:
    ///  * an entry only ever matches a part with the same type name,
    ///  * only matches within the tolerance,
    ///  * assignment is one-to-one: the closest pair wins, then the next closest that does not
    ///    reuse an already taken entry or part. This is what keeps stacked identical parts apart.
    /// </summary>
    public static class PartMatcher
    {
        /// <summary>
        /// Returns an array parallel to the entries: the index of the matched candidate, or -1.
        /// </summary>
        public static int[] Assign(string[] entryNames, float[] entryX, float[] entryY,
                                   string[] partNames, float[] partX, float[] partY,
                                   float tolerance)
        {
            int entryCount = entryNames != null ? entryNames.Length : 0;
            int[] result = new int[entryCount];
            for (int i = 0; i < entryCount; i++)
                result[i] = -1;

            int partCount = partNames != null ? partNames.Length : 0;
            if (entryCount == 0 || partCount == 0)
                return result;

            List<Pair> pairs = new List<Pair>();

            for (int e = 0; e < entryCount; e++)
            {
                for (int c = 0; c < partCount; c++)
                {
                    if (partNames[c] == null || entryNames[e] == null)
                        continue;
                    if (partNames[c] != entryNames[e])
                        continue;

                    float dx = partX[c] - entryX[e];
                    float dy = partY[c] - entryY[e];
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance > tolerance)
                        continue;

                    Pair pair = new Pair();
                    pair.entry = e;
                    pair.part = c;
                    pair.distance = distance;
                    pairs.Add(pair);
                }
            }

            pairs.Sort(delegate (Pair a, Pair b) { return a.distance.CompareTo(b.distance); });

            bool[] used = new bool[partCount];
            foreach (Pair pair in pairs)
            {
                if (result[pair.entry] != -1 || used[pair.part])
                    continue;

                result[pair.entry] = pair.part;
                used[pair.part] = true;
            }

            return result;
        }

        private class Pair
        {
            public int entry;
            public int part;
            public float distance;
        }
    }
}
