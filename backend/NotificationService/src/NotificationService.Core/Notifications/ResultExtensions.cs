using CSharpFunctionalExtensions;
using SharedKernel;

namespace NotificationService.Core.Notifications;

/// <summary>
/// Helper'ы для безопасного извлечения текста ошибки из <see cref="Result{T, E}"/> при логировании.
/// Защищает от NullReferenceException, если Error по какой-то причине пустой
/// (например, NSubstitute возвращает default(Result) в тестах).
/// </summary>
internal static class ResultExtensions
{
    public static string ErrorText<T>(this Result<T, Error> result) =>
        result.IsSuccess
            ? "ok"
            : (result.Error?.Messages is { Count: > 0 } msgs ? msgs[0].Message : "no error message");
}
