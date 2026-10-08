using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.CourseMaterials.UseCases;
using EducationContentService.Core.Features.ModuleItems.UseCases;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     Интеграционные тесты сценариев из MATERIAL_LIFECYCLE.md.
///     Покрывают инварианты INV-3, INV-4, INV-8, INV-10 и сценарии 5, 6, 7, 8.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialLifecycleTests : EducationContentServiceTestsBase
{
    public MaterialLifecycleTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // Сценарий 8: создание из контекста модуля — атомарно создаются Material + course_materials + module_items.

    [Fact]
    public async Task Scenario8_CreateMaterial_WithModuleId_AtomicallyAttachesToCourseAndModule()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        (Guid courseId, Guid moduleId) = await CreateCourseWithModuleAsync(authorId, ct);

        var request = new CreateMaterialRequest(
            "Сценарий 8",
            "# тело",
            MaterialKind.ARTICLE.ToString(),
            AccessType.ENROLLED.ToString(),
            DraftId: null,
            CourseId: courseId,
            ModuleId: moduleId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials.FirstOrDefaultAsync(
                m => m.Title == Title.Create("Сценарий 8").Value, ct);
            Assert.NotNull(material);
            Assert.Equal(AccessType.ENROLLED, material.AccessType);

            bool inCourse = await db.CourseMaterials.AnyAsync(
                cm => cm.CourseId == courseId && cm.MaterialId == material.Id, ct);
            Assert.True(inCourse);

            bool inModule = await db.ModuleItems.AnyAsync(
                mi => mi.ModuleId == moduleId
                      && mi.ReferenceId == material.Id
                      && mi.ItemType == ModuleItemType.Material, ct);
            Assert.True(inModule);
        });
    }

    // Сценарий 6: один материал в нескольких модулях одного курса.

    [Fact]
    public async Task Scenario6_SameMaterial_InMultipleModulesOfSameCourse_OneCourseMaterialRow()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        (Guid courseId, Guid mod1) = await CreateCourseWithModuleAsync(authorId, ct);
        Guid mod2 = await AddModuleToCourseAsync(authorId, courseId, ct);

        // Создаём материал привязанный к курсу.
        Guid materialId = await CreateMaterialAttachedToCourseAsync(authorId, courseId, ct: ct);

        // Привязываем к двум модулям.
        HttpResponseMessage attach1 = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{mod1}/materials", new AttachMaterialToModuleRequest(materialId), ct);
        attach1.EnsureSuccessStatusCode();

        HttpResponseMessage attach2 = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{mod2}/materials", new AttachMaterialToModuleRequest(materialId), ct);
        attach2.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            int courseMaterialCount = await db.CourseMaterials
                .CountAsync(cm => cm.CourseId == courseId && cm.MaterialId == materialId, ct);
            Assert.Equal(1, courseMaterialCount); // ровно одна запись в course_materials

            int moduleItemCount = await db.ModuleItems.CountAsync(
                mi => mi.ReferenceId == materialId
                      && mi.ItemType == ModuleItemType.Material
                      && (mi.ModuleId == mod1 || mi.ModuleId == mod2), ct);
            Assert.Equal(2, moduleItemCount); // две привязки в module_items
        });
    }

    // INV-4: AttachMaterialToModule auto-создаёт course_materials, если его не было.

    [Fact]
    public async Task Inv4_AttachMaterialToModule_AutoCreates_CourseMaterial_If_Missing()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        (Guid courseId, Guid moduleId) = await CreateCourseWithModuleAsync(authorId, ct);

        // Создаём space-level материал (без привязки к курсу).
        Guid materialId = await CreateSpaceLevelMaterialAsync(authorId, "Space-level material", ct);

        // Сами по себе course_materials не существуют.
        await ExecuteInDb(async db =>
        {
            bool exists = await db.CourseMaterials.AnyAsync(
                cm => cm.CourseId == courseId && cm.MaterialId == materialId, ct);
            Assert.False(exists);
        });

        // Прикрепляем к модулю.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/materials",
            new AttachMaterialToModuleRequest(materialId),
            ct);
        response.EnsureSuccessStatusCode();

        // Теперь course_materials появилась автоматически.
        await ExecuteInDb(async db =>
        {
            bool createdCm = await db.CourseMaterials.AnyAsync(
                cm => cm.CourseId == courseId && cm.MaterialId == materialId, ct);
            Assert.True(createdCm);

            bool inModule = await db.ModuleItems.AnyAsync(
                mi => mi.ModuleId == moduleId
                      && mi.ReferenceId == materialId
                      && mi.ItemType == ModuleItemType.Material, ct);
            Assert.True(inModule);
        });
    }

    // Сценарий 5 (после #77): detach последнего курса больше НЕ даунгрейдит AccessType.
    // Orphan ENROLLED легитимен — гейт уходит на global full-access (`plan:all`).
    // module_items материала в этом курсе удаляются каскадно (без изменений).

    [Fact]
    public async Task Scenario5_DetachFromLastCourse_KeepsEnrolledAccessType_AndCascadesModuleItems()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        (Guid courseId, Guid moduleId) = await CreateCourseWithModuleAsync(authorId, ct);
        Guid materialId = await CreateMaterialAttachedToCourseAsync(
            authorId, courseId, accessType: AccessType.ENROLLED, ct: ct);

        HttpResponseMessage attach = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/materials", new AttachMaterialToModuleRequest(materialId), ct);
        attach.EnsureSuccessStatusCode();

        // Открепляем от курса.
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId}/materials/{materialId}", ct);
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials.FirstOrDefaultAsync(m => m.Id == materialId, ct);
            Assert.NotNull(material);
            // После #77: AccessType сохраняется (orphan ENROLLED легитимен).
            Assert.Equal(AccessType.ENROLLED, material.AccessType);

            // course_materials удалена.
            bool cmExists = await db.CourseMaterials.AnyAsync(
                cm => cm.CourseId == courseId && cm.MaterialId == materialId, ct);
            Assert.False(cmExists);

            // module_items материала в модуле этого курса — удалены каскадно.
            bool miExists = await db.ModuleItems.AnyAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == materialId, ct);
            Assert.False(miExists);
        });
    }

    // После #77 INV-3 снята — orphan ENROLLED легитимен (гейт через платформенный plan:all).

    [Fact]
    public async Task UpdateMaterial_SpaceLevelToEnrolled_Succeeds_AfterPlanBoundRefactor()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid materialId = await CreateSpaceLevelMaterialAsync(authorId, "Space material", ct);

        var request = new UpdateMaterialRequest(
            "Space material",
            "# обновлено",
            MaterialKind.ARTICLE.ToString(),
            AccessType.ENROLLED.ToString());

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/materials/{materialId}", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials
                .FirstOrDefaultAsync(m => m.Id == materialId, ct);
            Assert.NotNull(material);
            Assert.Equal(AccessType.ENROLLED, material.AccessType);
        });
    }

    // AttachMaterialToCourse — идемпотентен при повторном вызове.

    [Fact]
    public async Task AttachMaterialToCourse_AlreadyAttached_ReturnsSuccess()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        (Guid courseId, _) = await CreateCourseWithModuleAsync(authorId, ct);
        Guid materialId = await CreateMaterialAttachedToCourseAsync(authorId, courseId, ct: ct);

        // Повторно прикрепляем.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/materials",
            new AttachMaterialToCourseRequest(materialId),
            ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            int cmCount = await db.CourseMaterials.CountAsync(
                cm => cm.CourseId == courseId && cm.MaterialId == materialId, ct);
            Assert.Equal(1, cmCount); // дубликата нет
        });
    }

    // ===== Хелперы =====

    private async Task<(Guid courseId, Guid moduleId)> CreateCourseWithModuleAsync(Guid authorId, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        Guid moduleId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {Guid.CreateVersion7():N}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value, SortKey.Initial());
            db.Courses.Add(course);

            var module = new Module(authorId, Title.Create($"Модуль {Guid.CreateVersion7():N}").Value);
            db.Modules.Add(module);

            db.CourseItems.Add(new CourseItem(
                course.Id, CourseItemType.Module, module.Id, SortKey.Initial(), isOptional: false));

            await db.SaveChangesAsync(ct);
            courseId = course.Id;
            moduleId = module.Id;
        });

        return (courseId, moduleId);
    }

    private async Task<Guid> AddModuleToCourseAsync(Guid authorId, Guid courseId, CancellationToken ct)
    {
        Guid moduleId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(authorId, Title.Create($"Модуль 2 {Guid.CreateVersion7():N}").Value);
            db.Modules.Add(module);

            // Sort_key должен идти после первого модуля.
            CourseItem? lastItem = await db.CourseItems
                .Where(ci => ci.CourseId == courseId && ci.ItemType == CourseItemType.Module)
                .OrderByDescending(ci => ci.SortKey)
                .FirstOrDefaultAsync(ct);

            SortKey sortKey = lastItem is null
                ? SortKey.Initial()
                : SortKey.After(lastItem.SortKey);

            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, module.Id, sortKey, isOptional: false));

            await db.SaveChangesAsync(ct);
            moduleId = module.Id;
        });
        return moduleId;
    }

    private async Task<Guid> CreateSpaceLevelMaterialAsync(Guid authorId, string title, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create("# контент").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateMaterialAttachedToCourseAsync(
        Guid authorId,
        Guid courseId,
        AccessType accessType = AccessType.ENROLLED,
        CancellationToken ct = default)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId, Title.Create($"Материал {Guid.CreateVersion7():N}").Value,
                MaterialKind.ARTICLE, accessType);
            material.Update(
                material.Title,
                MarkdownContent.Create("# контент").Value,
                MaterialKind.ARTICLE,
                accessType,
                boundCourseCount: 1,
                description: null);
            db.Materials.Add(material);

            db.CourseMaterials.Add(new CourseMaterial(courseId, material.Id, SortKey.Initial()));

            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }
}
