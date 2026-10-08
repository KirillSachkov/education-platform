namespace AccessService.Domain;

/// <summary>
/// Привязка курса к <see cref="Plan"/> — child-сущность в nav-collection <c>Plan.Courses</c>
/// (bundle-модель, #404). COURSE-tier план покрывает один или несколько курсов; каждая
/// привязка — отдельная строка в <c>access.plan_courses</c>. Уникальность <c>(plan_id, course_id)</c>
/// гарантируется индексом, дедуп — на уровне домена (<see cref="Plan.SetCourses"/>).
/// </summary>
public sealed class PlanCourse
{
    private PlanCourse() { } // EF

    private PlanCourse(Guid id, Guid planId, Guid courseId)
    {
        Id = id;
        PlanId = planId;
        CourseId = courseId;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid CourseId { get; private set; }

    /// <summary>
    /// Factory для child-сущности в nav-collection. <c>Id = Guid.Empty</c> — EF заполнит его
    /// через <c>TimeOrderedGuidValueGenerator</c> на Add; ручной <c>Guid.CreateVersion7()</c>
    /// сломал бы change-tracker discovery (entity → Modified вместо Added → 0-row UPDATE →
    /// DbUpdateConcurrencyException). См. инцидент 2026-05-08, docs/agents/backend-transactions.md правило 4.
    /// Domain event здесь не рейзится — его поднимает <see cref="Plan"/> в bundle-методах.
    /// </summary>
    internal static PlanCourse Create(Guid planId, Guid courseId) =>
        new(Guid.Empty, planId, courseId);
}
