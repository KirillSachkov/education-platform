using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.IntegrationTests.Features.Lifecycle;

/// <summary>
///     L1: cleanup на <c>quiz.hard_deleted</c> (ST-13 #493) — попытки квиза
///     (<c>quiz_attempts</c>) и пункты прогресса модулей (<c>module_item_progress</c>
///     item_type=QUIZ) сносятся; соседние квизы/материалы не затрагиваются.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class QuizHardDeletedHandlerTests : ProgressServiceTestsBase
{
    public QuizHardDeletedHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task QuizHardDeleted_ShouldDeleteAttempts_AndModuleItemProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid quizId = Guid.NewGuid();
        Guid otherQuizId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);

            // Попытки удаляемого квиза (двух юзеров) + «соседний» квиз для проверки изоляции.
            QuizAttemptAnswer answer = QuizAttemptAnswer.Create(
                Guid.NewGuid(), selectedOptionIds: [Guid.NewGuid()], textAnswer: null).Value;
            db.QuizAttempts.Add(QuizAttempt.Create(
                userId, quizId, [answer], scorePercent: 100, passed: true).Value);
            db.QuizAttempts.Add(QuizAttempt.Create(
                otherUserId, quizId, [answer], scorePercent: 30, passed: false).Value);
            db.QuizAttempts.Add(QuizAttempt.Create(
                userId, otherQuizId, [answer], scorePercent: 100, passed: true).Value);

            ModuleProgress moduleProgress = ModuleProgress.Create(enrollment.Id, moduleId, itemsTotal: 3).Value;
            db.ModuleProgresses.Add(moduleProgress);

            // Пункты модуля: удаляемый квиз, соседний квиз, материал.
            db.ModuleItemProgresses.Add(ModuleItemProgress.CreateQuizProgress(
                enrollment.Id, moduleId, quizId).Value);
            db.ModuleItemProgresses.Add(ModuleItemProgress.CreateQuizProgress(
                enrollment.Id, moduleId, otherQuizId).Value);
            db.ModuleItemProgresses.Add(ModuleItemProgress.CreateMaterialProgress(
                enrollment.Id, moduleId, materialId).Value);

            await db.SaveChangesAsync();
        });

        await InvokeMessageAndWaitAsync(new QuizHardDeleted(quizId));

        // Попытки удалены только для удаляемого квиза (у обоих юзеров).
        int attemptsForDeleted = await ExecuteInDb(db =>
            db.QuizAttempts.CountAsync(x => x.QuizId == quizId));
        Assert.Equal(0, attemptsForDeleted);

        int otherQuizAttempts = await ExecuteInDb(db =>
            db.QuizAttempts.CountAsync(x => x.QuizId == otherQuizId));
        Assert.Equal(1, otherQuizAttempts);

        // module_item_progress удалён только для удаляемого квиза.
        int moduleItemsForDeleted = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == quizId));
        Assert.Equal(0, moduleItemsForDeleted);

        int otherModuleItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ReferenceId == otherQuizId || x.ReferenceId == materialId));
        Assert.Equal(2, otherModuleItems);
    }

    [Fact]
    public async Task QuizHardDeleted_WithoutAnyQuizData_ShouldBeNoOp()
    {
        Guid quizId = Guid.NewGuid();

        // Никаких записей нет — handler должен молча отработать.
        await InvokeMessageAndWaitAsync(new QuizHardDeleted(quizId));

        int attempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync());
        Assert.Equal(0, attempts);
    }
}
