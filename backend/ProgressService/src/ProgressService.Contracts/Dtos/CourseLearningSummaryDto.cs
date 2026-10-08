namespace ProgressService.Contracts.Dtos;

public sealed record CourseLearningSummaryDto(
    int TotalModules,
    int MaterialsTotal,
    int MaterialsViewed,
    int ModulesCompleted,
    int IssuesTotal,
    int IssuesCompleted,
    int TotalItems,
    int CompletedItems,
    int ProgressPercent);
