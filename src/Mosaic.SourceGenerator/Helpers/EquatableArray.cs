using System.Collections;
using System.Collections.Immutable;

namespace Mosaic.SourceGenerator.Helpers;

/// <summary>
/// Wrapper around <see cref="ImmutableArray{T}"/> that implements value equality.
/// Required for incremental source-generator pipelines: the SDK caches outputs based on equality
/// of intermediate values, and <see cref="ImmutableArray{T}"/> uses reference equality by default.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array.IsDefault ? ImmutableArray<T>.Empty : array;

    public EquatableArray(IEnumerable<T> source) => _array = source.ToImmutableArray();

    public int Count => _array.Length;

    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.Length != other._array.Length)
        {
            return false;
        }

        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var item in _array)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_array).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public ImmutableArray<T> AsImmutableArray() => _array;

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
