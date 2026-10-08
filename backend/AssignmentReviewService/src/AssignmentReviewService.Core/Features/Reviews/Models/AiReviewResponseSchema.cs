using Shared.AI;

namespace AssignmentReviewService.Core.Features.Reviews.Models;

/// <summary>
///     JSON-schema для structured-output AI review response. Передаётся в
///     <see cref="AiGenerationRequest.JsonSchema"/> + <c>OutputMode = JsonSchema</c>.
///     Provider гарантирует что ответ распарсится в <see cref="AiReviewResponseDto"/>.
///     Вариант с <c>need_files</c> (#798) используется только когда включён дозапрос
///     файлов репозитория — иначе схема байт-в-байт прежняя.
/// </summary>
public static class AiReviewResponseSchema
{
    public const string SCHEMA_NAME = "ai_review_response";

    private const string VERDICT_PROPERTY = """
            "verdict": {
              "type": "string",
              "enum": ["LOOKS_GOOD", "MINOR_ISSUES", "MAJOR_ISSUES", "OFF_TOPIC"],
              "description": "Итоговая оценка PR относительно задания. LOOKS_GOOD: всё ок. MINOR_ISSUES: мелкие замечания. MAJOR_ISSUES: серьёзные проблемы. OFF_TOPIC: PR не решает задание."
            },
            "summary": {
              "type": "string",
              "description": "ОЧЕНЬ краткая выжимка: 1-3 предложения, до ~400 символов. Главный вывод + что доработать в двух словах. Без воды, без списков. Детальные замечания НЕ сюда, а в inline_comments. На русском."
            },
            "inline_comments": {
              "type": "array",
              "description": "Inline-комментарии. До 12 штук. Каждый — точечный фидбэк по конкретной строке/блоку.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["path", "line", "body"],
                "properties": {
                  "path": {
                    "type": "string",
                    "description": "Путь к файлу относительно корня репозитория, ровно как в diff'е."
                  },
                  "line": {
                    "type": "integer",
                    "description": "Номер строки в new (post-diff) версии файла. Должен попадать в hunk."
                  },
                  "body": {
                    "type": "string",
                    "description": "Тело комментария на русском, 1-3 предложения. Markdown допустим."
                  },
                  "suggestion": {
                    "type": ["string", "null"],
                    "description": "Опциональный inline-suggestion (GitHub suggestion block) — корректный код для замены."
                  }
                }
              }
            }
    """;

    public const string SCHEMA = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["verdict", "summary", "inline_comments"],
          "properties": {
        {{VERDICT_PROPERTY}}
          }
        }
        """;

    /// <summary>
    ///     Схема с полем <c>need_files</c> (#798): модель либо выносит вердикт
    ///     (need_files = []), либо просит файлы репозитория вместо гадания.
    /// </summary>
    public const string SCHEMA_WITH_NEED_FILES = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["verdict", "summary", "inline_comments", "need_files"],
          "properties": {
        {{VERDICT_PROPERTY}},
            "need_files": {
              "type": "array",
              "items": { "type": "string" },
              "description": "ПУСТОЙ массив, если вердикт можно вынести уверенно. Непустой — только когда решающий для вердикта код лежит ВНЕ диффа (например, определение символа): перечисли пути файлов из дерева репозитория, и тебя вызовут повторно с их содержимым. Не проси уже предоставленные файлы."
            }
          }
        }
        """;

    public static AiJsonSchema Build(bool includeNeedFiles = false) =>
        new(
            SCHEMA_NAME,
            includeNeedFiles ? SCHEMA_WITH_NEED_FILES : SCHEMA,
            "Structured AI review для student PR — verdict, summary, inline-комменты.",
            Strict: true);
}

/// <summary>
///     DTO, в который parser десериализует JSON ответ AI. Содержит сырое
///     представление; <c>AiReviewer.Parse</c> преобразует это в
///     <see cref="ParsedAiReview"/> с типизированным verdict.
/// </summary>
public sealed record AiReviewResponseDto(
    string Verdict,
    string Summary,
    IReadOnlyList<AiReviewInlineCommentDto> InlineComments,
    IReadOnlyList<string>? NeedFiles = null);

public sealed record AiReviewInlineCommentDto(
    string Path,
    int Line,
    string Body,
    string? Suggestion);
