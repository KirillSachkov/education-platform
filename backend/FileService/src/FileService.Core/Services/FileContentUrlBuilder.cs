using Microsoft.Extensions.Options;

namespace FileService.Core.Services;

public sealed class FileContentUrlBuilder
{
    private readonly FilePublicUrlOptions _options;

    public FileContentUrlBuilder(IOptions<FilePublicUrlOptions> options)
    {
        _options = options.Value;
    }

    public string Build(Guid assetId) => $"{_options.BasePath.TrimEnd('/')}/{assetId:D}/content";
}
