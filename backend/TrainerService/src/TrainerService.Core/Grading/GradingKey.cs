namespace TrainerService.Core.Grading;

/// <summary>
///     Server-only снапшот ключа грейдинга одного вопроса, сериализуется в
///     <c>TrainingSessionItem.GradingKeyJson</c> (jsonb) при выдаче вопроса.
///     НИКОГДА не попадает в response DTO — читается только грейдером.
/// </summary>
public sealed record GradingKey(
    IReadOnlyList<Guid> CorrectOptionIds,
    string? ReferenceAnswer,
    string? Explanation);
