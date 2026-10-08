using Amazon.S3;
using FileService.Domain;
using SharedKernel;

namespace FileService.Infrastructure.S3;

public static class S3ErrorMapper
{
    public static Error ToError(Exception ex) => ex switch
    {
        AmazonS3Exception { ErrorCode: "NoSuchBucket" }
            => FileErrors.BucketNotFound(),

        AmazonS3Exception { ErrorCode: "AccessDenied" or "SignatureDoesNotMatch" or "InvalidAccessKeyId" }
            => FileErrors.Forbidden(),

        AmazonS3Exception { ErrorCode: "InvalidRequest" or "InvalidArgument" }
            => FileErrors.ValidationFailed(),

        AmazonS3Exception { ErrorCode: "InternalError" }
            => FileErrors.InternalServerError(),

        AmazonS3Exception { ErrorCode: "NoSuchKey" }
            => FileErrors.ObjectNotFound(),

        AmazonS3Exception { ErrorCode: "NoSuchUpload" }
            => FileErrors.UploadNotFound(),

        ArgumentException
            => FileErrors.ValidationFailed(),

        HttpRequestException
            => FileErrors.NetworkIssue(),

        OperationCanceledException
            => FileErrors.OperationCanceled(),

        _ => FileErrors.Unknown()
    };
}