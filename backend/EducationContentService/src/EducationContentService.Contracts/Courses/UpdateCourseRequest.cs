using System.Text.Json.Serialization;
using EducationContentService.Contracts.Common;

namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Запрос на частичное обновление курса (PATCH-семантика).
///     Каждое поле — <see cref="Optional{T}"/>: отсутствие в JSON-теле означает «не трогать»,
///     присутствие со значением (включая <c>null</c>) — установить.
/// </summary>
/// <remarks>
///     <para>
///         <c>[property: JsonIgnore(...WhenWritingDefault)]</c> на каждом параметре:
///         когда .NET-клиент сериализует запрос с <c>default</c>-полем (Optional с IsSet=false),
///         поле полностью опускается в JSON, а не пишется как <c>null</c>. Иначе сервер бы
///         трактовал отсутствие поля и явный null одинаково — теряется PATCH-семантика.
///     </para>
///     <para>
///         Семантика по полям:
///         <list type="bullet">
///             <item><see cref="Title"/>, <see cref="Description"/> — non-empty при установке.</item>
///             <item><see cref="GettingStartedModuleId"/> — <c>null</c> сбрасывает стартовый модуль.</item>
///             <item><see cref="PreviewId"/>, <see cref="VideoId"/> — <c>null</c> детачит обложку/трейлер
///                   через FileService (sync bind).</item>
///         </list>
///     </para>
/// </remarks>
public sealed record UpdateCourseRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<string> Title = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<string> Description = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<Guid?> GettingStartedModuleId = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<string> Slug = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<Guid?> PreviewId = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<Guid?> VideoId = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<IReadOnlyList<string>?> LearningOutcomes = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<IReadOnlyList<string>?> TargetAudience = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<IReadOnlyList<string>?> Prerequisites = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<bool> ShowInFullAccess = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Optional<string> Kind = default);
