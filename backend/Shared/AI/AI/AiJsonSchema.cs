namespace Shared.AI;

public sealed record AiJsonSchema(
    string Name,
    string Schema,
    string? Description = null,
    bool Strict = true);
