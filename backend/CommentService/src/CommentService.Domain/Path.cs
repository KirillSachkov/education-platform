namespace CommentService.Domain;

/// <summary>
/// Представляет иерархический путь элемента / Represents a hierarchical path of an element.
/// </summary>
public sealed record Path
{
    /// <summary>
    /// Разделитель элементов в пути / Separator for elements in the path.
    /// </summary>
    private const char SEPARATOR = '.';

    /// <summary>
    /// Начальная глубина для родительского элемента / Starting depth for a parent element.
    /// </summary>
    private const short START_DEPTH = 0;

    private Path(string value, short depth)
    {
        Value = value;
        Depth = depth;
    }

    /// <summary>
    /// Строковое представление пути / String representation of the path.
    /// </summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>
    /// Глубина вложенности элемента в иерархии / Nesting depth of the element in the hierarchy.
    /// </summary>
    public short Depth { get; private set; }

    /// <summary>
    /// Создает родительский путь с указанным идентификатором / Creates a parent path with the specified identifier.
    /// </summary>
    /// <param name="id">Идентификатор родительского элемента / Parent element identifier.</param>
    /// <returns>Новый экземпляр пути с глубиной 0 / New path instance with depth 0.</returns>
    public static Path CreateParent(Guid id)
    {
        return new Path(id.ToString(), START_DEPTH);
    }

    /// <summary>
    /// Создает дочерний путь на основе текущего пути / Creates a child path based on the current path.
    /// </summary>
    /// <param name="id">Идентификатор дочернего элемента / Child element identifier.</param>
    /// <returns>Новый экземпляр пути с увеличенной глубиной / New path instance with increased depth.</returns>
    public Path CreateChild(Guid id)
    {
        string path = $"{Value}{SEPARATOR}{id.ToString()}";

        string[] depth = path.Split(SEPARATOR);

        return new Path(path, (short)(depth.Length - 1));
    }
}