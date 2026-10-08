using System.Text.Json;
using System.Text.Json.Serialization;

namespace EducationContentService.Contracts.Common;

/// <summary>
///     Wrapper для частичного обновления (PATCH-семантика).
///     Различает три состояния поля в JSON-теле запроса:
///     <list type="bullet">
///         <item>отсутствует (<see cref="IsSet"/> = false) — не трогать поле в БД,</item>
///         <item>присутствует и равно <c>null</c> (<see cref="IsSet"/> = true, <see cref="Value"/> = null) — явная очистка,</item>
///         <item>присутствует и имеет значение (<see cref="IsSet"/> = true, <see cref="Value"/> = X) — установить.</item>
///     </list>
///     Записи с positional-параметрами получают <c>default</c> для отсутствующих полей,
///     что соответствует <c>IsSet = false</c>. Implicit-conversion из <typeparamref name="T"/>
///     обеспечивает обратную совместимость с тестами и call-sites,
///     которые передают значения напрямую.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    public Optional(T value)
    {
        IsSet = true;
        Value = value;
    }

    public bool IsSet { get; }

    public T Value { get; }

    public T Or(T fallback) => IsSet ? Value : fallback;

    public static Optional<T> Of(T value) => new(value);

    /// <summary>
    ///     Named alternate to the implicit conversion (CA2225 compliance).
    /// </summary>
    public static Optional<T> FromT(T value) => new(value);

    public static implicit operator Optional<T>(T value) => new(value);

    public bool Equals(Optional<T> other) =>
        IsSet == other.IsSet && EqualityComparer<T>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(IsSet, Value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(Value));

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
}

/// <summary>
///     Factory-конвертер: System.Text.Json резолвит конкретный <see cref="OptionalJsonConverter{T}"/>
///     для каждого <c>T</c>, чтобы корректно обрабатывать generic-обёртку.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type valueType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(OptionalJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
///     Конвертер, читающий/пишущий <see cref="Optional{T}"/>:
///     отсутствие поля в JSON обрабатывается на уровне родителя (получаем <c>default</c>),
///     а здесь — только присутствующее значение, включая явный <c>null</c>.
/// </summary>
public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        T? value = JsonSerializer.Deserialize<T>(ref reader, options);
        return new Optional<T>(value!);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        // Properties carrying [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        // never reach Write when IsSet=false; throwing surfaces bugs where a caller forgot
        // the attribute and would otherwise silently emit null (== explicit clear).
        if (!value.IsSet)
        {
            throw new InvalidOperationException(
                "Cannot serialize Optional<T> with IsSet=false. " +
                "Add [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] " +
                "to the property to skip emitting unset values.");
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
