using System.Data;
using System.Text.Json;
using Dapper;

namespace AuthService.Infrastructure.Postgres;

/// <summary>Dapper TypeHandler для JSONB-колонок PostgreSQL.</summary>
public sealed class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override T? Parse(object value) =>
        value is string s ? JsonSerializer.Deserialize<T>(s, JsonTypeHandlerOptions.s_default) : default;

    public override void SetValue(IDbDataParameter parameter, T? value) =>
        parameter.Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, JsonTypeHandlerOptions.s_default);
}

internal static class JsonTypeHandlerOptions
{
    internal static readonly JsonSerializerOptions s_default = new() { PropertyNameCaseInsensitive = true };
}
