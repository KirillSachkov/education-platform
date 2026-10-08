using Microsoft.Extensions.Options;

namespace FileService.Core.FilesStorage;

public sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add("FileStorageOptions:Endpoint не может быть пустым.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            failures.Add("FileStorageOptions:AccessKey не может быть пустым.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add("FileStorageOptions:SecretKey не может быть пустым.");
        }

        if (options.DownloadUrlExpirationMinutes <= 60)
        {
            failures.Add("FileStorageOptions:DownloadUrlExpirationMinutes должен быть больше 60.");
        }

        if (options.ProtectedDownloadUrlExpirationMinutes <= 0)
        {
            failures.Add("FileStorageOptions:ProtectedDownloadUrlExpirationMinutes должен быть больше 0.");
        }

        if (options.ProtectedDownloadUrlExpirationMinutes > options.DownloadUrlExpirationMinutes)
        {
            failures.Add("FileStorageOptions:ProtectedDownloadUrlExpirationMinutes не должен превышать DownloadUrlExpirationMinutes.");
        }

        if (options.UploadUrlExpirationMinutes <= 0)
        {
            failures.Add("FileStorageOptions:UploadUrlExpirationMinutes должен быть больше 0.");
        }

        if (options.MaxConcurrentRequests <= 0)
        {
            failures.Add("FileStorageOptions:MaxConcurrentRequests должен быть больше 0.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
