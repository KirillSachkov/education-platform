namespace TrainerService.Domain;

/// <summary>
/// Язык/стек трека тренажёра / Track language-stack. Верхний селектор хаба.
/// Extensible — новый стек = +1 член enum, миграция не нужна (string без CHECK).
/// </summary>
public enum TrackStack
{
    CSHARP,
    TYPESCRIPT,
    DEVOPS,
}
