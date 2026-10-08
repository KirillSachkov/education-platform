namespace Shared.AI;

public sealed record AiInputPart(
    AiInputPartType Type,
    string Name,
    string? Text,
    string? FileName,
    string? ContentType,
    ReadOnlyMemory<byte> Content)
{
    public static AiInputPart TextPart(string name, string text) =>
        new(AiInputPartType.Text, name, text, null, null, ReadOnlyMemory<byte>.Empty);

    public static AiInputPart FilePart(
        string name,
        string fileName,
        string contentType,
        byte[] content) =>
        new(AiInputPartType.File, name, null, fileName, contentType, content);

    public static AiInputPart AudioPart(
        string name,
        string fileName,
        string contentType,
        byte[] content) =>
        new(AiInputPartType.Audio, name, null, fileName, contentType, content);
}
