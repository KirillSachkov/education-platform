namespace TagService.Core;

public static class Constants
{
    public const int MIN_PAGE_SIZE = 1;
    public const int MAX_PAGE_LENGTH = 200;
    public const int MEDIUM_MAX_PAGE_LENGTH = MAX_PAGE_LENGTH / 2;
    public const int MAX_SEARCH_LOOKUP_BATCH_SIZE = 500;
    public const int MAX_TAGS_PER_MUTATION = 100;
    public const int MAX_CURSOR_LENGTH = 2048;

    public const string ANONYMOUS_READ_RATE_LIMIT_POLICY = "anonymous-read";
}
