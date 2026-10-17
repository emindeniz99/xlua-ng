// An immutable array that compares by its elements. The incremental pipeline
// caches a step's output when it is Equal to the previous one, and
// ImmutableArray<T> compares by reference, so a plain ImmutableArray would
// re-run every later step after any edit.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace XLua.SourceGenerator
{
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        public static readonly EquatableArray<T> Empty = new EquatableArray<T>(ImmutableArray<T>.Empty);

        readonly ImmutableArray<T> items;

        public EquatableArray(ImmutableArray<T> items)
        {
            this.items = items.IsDefault ? ImmutableArray<T>.Empty : items;
        }

        public ImmutableArray<T> Items
        {
            get { return items.IsDefault ? ImmutableArray<T>.Empty : items; }
        }

        public int Length
        {
            get { return Items.Length; }
        }

        public T this[int index]
        {
            get { return Items[index]; }
        }

        public bool Equals(EquatableArray<T> other)
        {
            var a = Items;
            var b = other.Items;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is EquatableArray<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var item in Items)
            {
                hash = unchecked(hash * 31 + (item == null ? 0 : item.GetHashCode()));
            }
            return hash;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal static class EquatableArray
    {
        public static EquatableArray<T> From<T>(IEnumerable<T> items) where T : IEquatable<T>
        {
            return new EquatableArray<T>(ImmutableArray.CreateRange(items));
        }
    }
}
