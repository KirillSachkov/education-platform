using System.Data;
using System.Text.Json;
using Dapper;

namespace Core.Database;

public class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = value is null
            ? DBNull.Value
            : JsonSerializer.Serialize(value);
    }

    public override T Parse(object value)
    {
        if (value is DBNull)
            return default!;

        string? jsonString = value as string;

        if (string.IsNullOrEmpty(jsonString))
            return default!;

        return JsonSerializer.Deserialize<T>(jsonString)!;
    }
}