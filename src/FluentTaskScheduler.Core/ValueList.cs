using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentTaskScheduler;

/// <summary>
/// An immutable list that equals another list with equal items. Records compare their members
/// with Equals, so a record that holds its lists as <see cref="ValueList{T}"/> compares by content
/// instead of by which list instance it happens to hold.
/// </summary>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
[JsonConverter(typeof(ValueListJsonConverter))]
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    public static ValueList<T> Empty { get; } = new([]);

    private readonly T[] _items;

    internal ValueList(T[] items) => _items = items;

    public int Count => _items.Length;
    public T this[int index] => _items[index];

    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.SequenceEqual(other._items));

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T item in _items) hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public static implicit operator ValueList<T>(T[] items) => ValueList.Create<T>(items);
    public static implicit operator ValueList<T>(List<T> items) => ValueList.Create<T>(CollectionsMarshal.AsSpan(items));

    public override string ToString() => $"[{string.Join(", ", _items)}]";
}

public static class ValueList
{
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) =>
        items.IsEmpty ? ValueList<T>.Empty : new ValueList<T>(items.ToArray());

    public static ValueList<T> ToValueList<T>(this IEnumerable<T> source) =>
        source as ValueList<T> ?? Create<T>(source.ToArray());
}

/// <summary>Reads and writes a <see cref="ValueList{T}"/> as a JSON array.</summary>
internal sealed class ValueListJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueList<>);

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(type.GetGenericArguments()))!;

    private sealed class Converter<T> : JsonConverter<ValueList<T>>
    {
        public override ValueList<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<T[]>(ref reader, options) is { } items ? ValueList.Create<T>(items) : ValueList<T>.Empty;

        public override void Write(Utf8JsonWriter writer, ValueList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }
}
