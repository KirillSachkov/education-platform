using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanCourseConfiguration : IEntityTypeConfiguration<PlanCourse>
{
    public void Configure(EntityTypeBuilder<PlanCourse> b)
    {
        b.ToTable("plan_courses");

        b.HasKey(c => c.Id);

        // Child в nav-collection (Plan.Courses) — Id заполняется EF через ValueGenerator
        // при Add; domain factory оставляет Id=Guid.Empty (см. docs/agents/backend-transactions.md
        // правило 4 + инцидент 2026-05-08).
        b.Property(c => c.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(c => c.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(c => c.CourseId)
            .HasColumnName("course_id")
            .IsRequired();

        // Один курс — одна привязка на план.
        b.HasIndex(c => new { c.PlanId, c.CourseId })
            .IsUnique()
            .HasDatabaseName("ux_plan_courses_plan_course");

        // Reverse lookup «какие планы покрывают курс X» (by-course-ids внутренний эндпоинт).
        b.HasIndex(c => c.CourseId)
            .HasDatabaseName("ix_plan_courses_course_id");
    }
}
