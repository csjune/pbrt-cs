// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// The C++ standard library algorithms that pbrt calls on data whose order
// matters (std::partition, std::nth_element and std::sort), with predicate
// and comparison structs so that calls don't allocate. The standard leaves
// the order of equivalent elements unspecified, and so do these: where
// there are ties (primitives with equal centroids, for example), the
// resulting order, and so a BVH or kd-tree built from it, can differ from
// that of a particular C++ standard library, while being just as valid.

namespace Pbrt;

public interface IStdPredicate<T>
{
    bool Evaluate(in T value);
}

public interface IStdLess<T>
{
    bool Less(in T a, in T b);
}

public static partial class Util
{
    // std::partition: moves the elements for which pred is true to the
    // front and returns the index of the first element for which it's
    // false.
    public static int StdPartition<T, P>(Span<T> s, P pred) where P : IStdPredicate<T>, allows ref struct
    {
        int mid = 0;
        for (int i = 0; i < s.Length; ++i)
            if (pred.Evaluate(s[i]))
            {
                if (i != mid)
                    (s[mid], s[i]) = (s[i], s[mid]);
                ++mid;
            }
        return mid;
    }

    // std::nth_element: s[nth] becomes the element that would be there if s
    // were sorted, with no larger elements before it and no smaller ones
    // after it. (Sorting the whole span satisfies that.)
    public static void StdNthElement<T, C>(Span<T> s, int nth, C comp) where C : IStdLess<T>
    {
        DCHECK(nth >= 0 && nth <= s.Length);
        StdSort(s, comp);
    }

    // std::sort
    public static void StdSort<T, C>(Span<T> s, C comp) where C : IStdLess<T> =>
        s.Sort(new LessComparer<T, C>(comp));

    private readonly struct LessComparer<T, C>(C less) : IComparer<T> where C : IStdLess<T>
    {
        public int Compare(T? a, T? b) => less.Less(a!, b!) ? -1 : less.Less(b!, a!) ? 1 : 0;
    }
}
