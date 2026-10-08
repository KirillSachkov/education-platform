namespace FileService.Domain;

public static class FileErrors
{
    public static Error BucketNotFound() => Error.NotFound("no.such.bucket", "Бакет не найден");

    public static Error UploadNotFound() =>
        Error.NotFound(
            "upload.not.found",
            "Сессия загрузки не найдена");

    public static Error ObjectNotFound(string? objectKey = null)
    {
        string key = objectKey is null ? string.Empty : $"с ключом {objectKey} ";
        return Error.NotFound("object.not.found", $"Объект {key}не найден");
    }

    public static Error Forbidden() => Error.Failure("access.denied", "Недостаточно прав для выполнения операции");

    public static Error ValidationFailed()
    {
        string message = "Запрос содержит некорректные данные";

        return Error.Validation("validation.failed", message);
    }

    public static Error InternalServerError() => Error.Failure("internal.server.error", "Внутренняя ошибка хранилища");

    public static Error OperationCanceled() => Error.Failure("operation.canceled", "Операция была отменена");

    public static Error NetworkIssue() =>
        Error.Failure(
            "network.issue",
            "Сетевая ошибка при взаимодействии с файловым хринилищем");

    public static Error Unknown() => Error.Failure("unknown.error", "Произошла неизвестная ошибка");

    public static Error HlsProcessingFailed() =>
        Error.Failure("hls.processing.failed", "Видеообработка завершилась с ошибкой");

    public static Error HlsProcessingFailed(string details) => Error.Failure(
        "hls.processing.failed",
        $"Видеообработка завершилась с ошибкой: {details}");

    public static Error ProcessFailed() => Error.Failure("process.failed", "Процесс завершился с ошибкой");

    public static Error InvalidFfprobeOutput(string details) =>
        Error.Failure("ffprobe.invalid.output", $"Невалидный вывод ffprobe: {details}");
}