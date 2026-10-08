using System.Security.Cryptography;
using System.Text;
using SearchService.Core.Reindex;
using Typesense;

namespace SearchService.Infrastructure.Typesense;

/// <summary>
/// Считает стабильный SHA-256 hash полей Typesense-схемы из <see cref="TypesenseSchemas"/>.
/// Любая правка схемы (поле добавлено/удалено/переименовано, изменён
/// <c>index</c>/<c>sort</c>/<c>facet</c>/<c>locale</c>/<c>stem</c>) меняет hash —
/// startup-check сравнивает его с <c>search.reindex_state.applied_schema_hash</c>
/// и сам триггерит полный реиндекс (#526). Ручной бамп generation не нужен.
/// </summary>
/// <remarks>
/// Сериализация каноническая (ручная, фиксированный порядок свойств) — не зависит
/// от версии Typesense-клиента и порядка свойств в его JSON-контракте. Имя
/// коллекции в hash не входит: оно timestamped при blue/green rebuild.
/// Незаданные nullable-флаги хэшируются как '-' (не схлопываются с false):
/// если апгрейд Typesense-клиента поменяет дефолты флагов, hash изменится и
/// прокатится один лишний (безвредный) реиндекс — осознанный trade-off в пользу
/// «лучше лишний rebuild, чем пропущенный».
/// </remarks>
public sealed class TypesenseSchemaVersionProvider : ISearchSchemaVersionProvider
{
    private const string CANONICAL_NAME_PLACEHOLDER = "schema-version";

    public string SchemaHash { get; } = ComputeHash();

    private static string ComputeHash()
    {
        Schema schema = TypesenseSchemas.CreateEducationSearchSchema(CANONICAL_NAME_PLACEHOLDER);

        var canonical = new StringBuilder();
        foreach (Field field in schema.Fields)
        {
            canonical
                .Append(field.Name).Append('|')
                .Append(field.Type.ToString()).Append('|')
                .Append(FormatFlag(field.Facet)).Append('|')
                .Append(FormatFlag(field.Optional)).Append('|')
                .Append(FormatFlag(field.Index)).Append('|')
                .Append(FormatFlag(field.Sort)).Append('|')
                .Append(FormatFlag(field.Infix)).Append('|')
                .Append(FormatFlag(field.Stem)).Append('|')
                .Append(field.Locale ?? string.Empty).Append(';');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(hash);
    }

    private static char FormatFlag(bool? value) => value switch
    {
        true => '1',
        false => '0',
        null => '-',
    };
}
