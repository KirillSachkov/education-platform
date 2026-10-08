namespace TagService.Domain;

public static class TagErrors
{
    public static Error TagAlreadyExists() =>
        Error.Conflict("tag.already.exists", "Тег с таким названием уже существует");

    public static Error AliasAlreadyAttached() =>
        Error.Conflict("tag.alias.already.exists", "Этот алиас уже привязан к тегу");

    public static Error EntityTagAlreadyAttached() =>
        Error.Conflict("tag.entity.already.exists", "Этот тег уже привязан к сущности");
}
