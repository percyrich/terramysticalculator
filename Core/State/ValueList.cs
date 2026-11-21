using System.Collections;
using System.Collections.Immutable;

namespace Core;

// An immutable list that compares by CONTENTS.
// ImmutableArray on its own compares by identity ("is it the same array object?"), which would make
// two identical game states count as different. Every list inside the game state uses this type, so
// record equality works all the way down without hand-written Equals methods.
// It is a struct, so a ValueList can never be null; an uninitialised one behaves as empty.
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly ImmutableArray<T> _items;

    public ValueList(IEnumerable<T> items) => _items = items.ToImmutableArray();
    private ValueList(ImmutableArray<T> items) => _items = items;

    public static readonly ValueList<T> Empty = new(ImmutableArray<T>.Empty);

    public static ValueList<T> Repeat(T value, int count) => new(Enumerable.Repeat(value, count));

    // default(ValueList<T>) holds no array at all; treat that as empty rather than crashing.
    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;
    public T this[int index] => Items[index];
    public int IndexOf(T item) => Items.IndexOf(item);
    public bool Contains(T item) => Items.Contains(item);

    // "Changes" return a new list; this one is untouched.
    public ValueList<T> SetItem(int index, T value) => new(Items.SetItem(index, value));
    public ValueList<T> Add(T item) => new(Items.Add(item));
    public ValueList<T> RemoveAt(int index) => new(Items.RemoveAt(index));
    public ValueList<T> Insert(int index, T item) => new(Items.Insert(index, item));

    public bool Equals(ValueList<T> other) => Items.SequenceEqual(other.Items);
    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }
    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);
    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[" + string.Join(", ", Items) + "]";
}

// Shorthand for building a list: ValueList.Of(a, b, c).
public static class ValueList
{
    public static ValueList<T> Of<T>(params T[] items) => new(items);
}
