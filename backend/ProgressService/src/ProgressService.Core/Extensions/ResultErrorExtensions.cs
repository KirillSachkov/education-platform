namespace ProgressService.Core.Extensions;

/// <summary>
///     Вспомогательные методы для проверки типа ошибки в Result без вложенных if.
/// </summary>
public static class ResultErrorExtensions
{
    /// <summary>
    ///     Возвращает <c>true</c>, если результат — неудача с типом <see cref="ErrorType.NOT_FOUND" />.
    /// </summary>
    public static bool IsNotFound<T>(this Result<T, Error> result)
        => result.IsFailure && result.Error.Type == ErrorType.NOT_FOUND;

    /// <summary>
    ///     Возвращает <c>true</c>, если результат — неудача с типом, отличным от <see cref="ErrorType.NOT_FOUND" />.
    /// </summary>
    public static bool IsFailureExceptNotFound<T>(this Result<T, Error> result)
        => result.IsFailure && result.Error.Type != ErrorType.NOT_FOUND;

    /// <summary>
    ///     Возвращает <c>true</c>, если результат — неудача с типом <see cref="ErrorType.FAILURE" />.
    ///     Такие ошибки считаются временными (инфраструктурные сбои, недоступность внешних сервисов)
    ///     и подлежат повторной обработке в Wolverine. Валидации, конфликты, not-found — не transient.
    /// </summary>
    public static bool IsTransientFailure<T>(this Result<T, Error> result)
        => result.IsFailure && result.Error.Type == ErrorType.FAILURE;

    /// <summary>
    ///     Возвращает <c>true</c>, если ошибка — временная (<see cref="ErrorType.FAILURE" />).
    /// </summary>
    public static bool IsTransientFailure(this UnitResult<Error> result)
        => result.IsFailure && result.Error.Type == ErrorType.FAILURE;
}
