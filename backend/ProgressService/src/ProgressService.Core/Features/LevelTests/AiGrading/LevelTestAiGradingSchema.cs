using Shared.AI;

namespace ProgressService.Core.Features.LevelTests.AiGrading;

/// <summary>
///     JSON-schema structured-output'а AI-грейдинга открытых ответов level-test'а
///     (ST-5, #480). Передаётся в <see cref="Shared.AI.Skills.StructuredExtractRequest"/>;
///     провайдер гарантирует, что ответ распарсится в <see cref="LevelTestAiGradesResponse"/>.
///     Диапазон score описан текстом (strict-режим не у всех провайдеров переваривает
///     minimum/maximum) — домен дополнительно клампит в <c>WithAiGrade</c>.
/// </summary>
public static class LevelTestAiGradingSchema
{
    public const string SCHEMA_NAME = "level_test_ai_grades";

    public const string SCHEMA = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["grades"],
          "properties": {
            "grades": {
              "type": "array",
              "description": "Оценки открытых ответов — ровно по одному элементу на каждый вопрос из входа.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["questionId", "score", "feedback"],
                "properties": {
                  "questionId": {
                    "type": "string",
                    "description": "UUID вопроса — копируй точно как во входных данных."
                  },
                  "score": {
                    "type": "integer",
                    "description": "Целое 0..100: 0 — ответ пустой/не по теме, 100 — полностью покрывает эталон."
                  },
                  "feedback": {
                    "type": "string",
                    "description": "Короткий фидбэк на русском, 1-3 предложения: что верно, чего не хватает."
                  }
                }
              }
            }
          }
        }
        """;

    public static AiJsonSchema Build() =>
        new(
            SCHEMA_NAME,
            SCHEMA,
            "Оценки открытых ответов level-test попытки.",
            Strict: true);
}

/// <summary>Typed payload structured-output'а: массив оценок открытых ответов.</summary>
public sealed record LevelTestAiGradesResponse(IReadOnlyList<LevelTestAiGradeItem> Grades);

/// <summary>Оценка одного открытого ответа: score 0..100 + короткий RU-фидбэк.</summary>
public sealed record LevelTestAiGradeItem(Guid QuestionId, int Score, string? Feedback);
