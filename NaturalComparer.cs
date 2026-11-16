using System;
using System.Collections.Generic;

namespace DirOpusReImagined
{
    /// <summary>
    /// Orders names the way people count: "file2" before "file10", "Track 9" before "Track 10",
    /// "photo.001" before "photo.002". Runs of digits compare by their value (any length, so long
    /// numbers don't overflow), everything else case-insensitively. Names equal by value (such as
    /// "a01" and "a1") still get a fixed order, so sorting never depends on where they started.
    /// </summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0, zerosTiebreak = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (si < x.Length && x[si] == '0') si++;
                    while (sj < y.Length && y[sj] == '0') sj++;
                    int ei = si, ej = sj;
                    while (ei < x.Length && char.IsAsciiDigit(x[ei])) ei++;
                    while (ej < y.Length && char.IsAsciiDigit(y[ej])) ej++;

                    // More significant digits means a bigger number.
                    int byLength = (ei - si).CompareTo(ej - sj);
                    if (byLength != 0) return byLength;
                    int byDigits = string.CompareOrdinal(x, si, y, sj, ei - si);
                    if (byDigits != 0) return byDigits;
                    // Same value: fewer leading zeros first, but only if nothing else differs.
                    if (zerosTiebreak == 0) zerosTiebreak = (si - i).CompareTo(sj - j);
                    i = ei; j = ej;
                    continue;
                }

                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }

            int rest = (x.Length - i).CompareTo(y.Length - j);
            if (rest != 0) return rest;
            if (zerosTiebreak != 0) return zerosTiebreak;
            // Equal apart from case: keep a fixed order.
            return string.CompareOrdinal(x, y);
        }
    }
}
