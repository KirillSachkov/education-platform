namespace EducationContentService.Core.Database;

/// <summary>
///     Простой билдер WHERE-клаузы для Dapper-запросов.
/// </summary>
public sealed class WhereBuilder
{
    private readonly List<string> _conditions = [];

    /// <summary>
    ///     Проверяет, есть ли условия.
    /// </summary>
    public bool HasConditions => _conditions.Count > 0;

    /// <summary>
    ///     Добавляет условие, если predicate == true.
    /// </summary>
    public WhereBuilder AddIf(bool predicate, string condition)
    {
        if (predicate)
        {
            _conditions.Add(condition);
        }

        return this;
    }

    /// <summary>
    ///     Добавляет условие безусловно.
    /// </summary>
    public WhereBuilder Add(string condition)
    {
        _conditions.Add(condition);
        return this;
    }

    /// <summary>
    ///     Возвращает WHERE-клаузу (с "WHERE " префиксом) или пустую строку, если условий нет.
    /// </summary>
    public string Build()
    {
        if (_conditions.Count == 0)
        {
            return string.Empty;
        }

        return "WHERE " + string.Join(" AND ", _conditions);
    }

    /// <summary>
    ///     Возвращает только условия (без "WHERE "), соединённые через AND.
    ///     Полезно для добавления к существующему WHERE.
    /// </summary>
    public string BuildConditions()
    {
        if (_conditions.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" AND ", _conditions);
    }

    /// <summary>
    ///     Возвращает условия как "AND ..." для добавления к существующему WHERE.
    ///     Возвращает пустую строку, если условий нет.
    /// </summary>
    public string BuildAsAnd()
    {
        if (_conditions.Count == 0)
        {
            return string.Empty;
        }

        return " AND " + string.Join(" AND ", _conditions);
    }
}