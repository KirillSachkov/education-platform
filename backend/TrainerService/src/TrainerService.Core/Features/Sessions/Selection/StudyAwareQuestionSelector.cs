using System.Security.Cryptography;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Core.Features.Sessions.Selection;

/// <summary>
///     Study-state-aware выбор вопросов для старта сессии (#691 t2) — общий для DRILL / LEARN / MOCK.
///     До этого старты были stateless (<c>Shuffle(pool).Take(N)</c>) и игнорировали
///     <see cref="QuestionStudyState"/> → вопросы повторялись из сессии в сессию. Теперь набор
///     приоритизируется по тому, что пользователь уже знает:
///     <list type="bullet">
///         <item><b>NEW</b> (нет строки study-state) + <b>WRONG/REVIEW</b> — в начало;</item>
///         <item>затем <b>SEEN</b>;</item>
///         <item><b>KNOWN</b> — в хвост (показываем только когда «свежий» пул исчерпан).</item>
///     </list>
///     Поверх приоритета — <b>исключение недавно виденных</b> (<see cref="QuestionStudyState.LastSeenAt"/>
///     в пределах <see cref="RecentlySeenWindow"/>): пока есть нетронутые/не-недавние кандидаты на
///     добор N, недавние не берутся. Если после исключения кандидатов меньше N — добор недавними
///     по принципу <i>least-recently-seen first</i> (раньше всего виденные → раньше всего «созревают»
///     на повтор). Внутри каждого приоритетного бакета — шафл (вариативность от сессии к сессии).
///     Финальный потолок — <c>min(N, доступных)</c>, как и в прежней логике.
///     <para>Чистая функция: фримиум-фильтрация пула (<c>IsFreeSample</c> для не-PRO) делается
///     вызывающим ДО передачи кандидатов сюда — селектор её не дублирует.</para>
/// </summary>
public static class StudyAwareQuestionSelector
{
    /// <summary>
    ///     Окно «недавно виденного»: вопрос с <see cref="QuestionStudyState.LastSeenAt"/> не старше
    ///     этого интервала исключается из набора, пока есть чем добрать N из «свежих». Здравый дефолт —
    ///     несколько часов: одна и та же тренировка подряд не выдаёт те же вопросы, но к следующему дню
    ///     они снова доступны (SRS-расписание двигает «созревание» отдельно).
    /// </summary>
    public static readonly TimeSpan RecentlySeenWindow = TimeSpan.FromHours(6);

    // Приоритетные бакеты (меньше = раньше в наборе).
    private const int PRIORITY_NEW_OR_WRONG = 0; // нет строки, либо WRONG/REVIEW
    private const int PRIORITY_SEEN = 1;
    private const int PRIORITY_KNOWN = 2; // в хвост

    /// <summary>
    ///     Выбирает до <paramref name="count"/> кандидатов из <paramref name="candidates"/> по
    ///     study-state-приоритету (см. описание класса). <paramref name="questionId"/> извлекает id
    ///     вопроса из кандидата (для DRILL/LEARN — сам вопрос, для MOCK — обёртка с темой).
    ///     <paramref name="statesByQuestion"/> — study-state'ы вызывающего по id кандидатов (отсутствие
    ///     ключа = NEW). <paramref name="now"/> — точка отсчёта окна «недавно виденного».
    /// </summary>
    public static IReadOnlyList<T> Select<T>(
        IReadOnlyList<T> candidates,
        Func<T, Guid> questionId,
        IReadOnlyDictionary<Guid, QuestionStudyState> statesByQuestion,
        int count,
        DateTimeOffset now)
    {
        if (candidates.Count == 0 || count <= 0)
            return [];

        DateTimeOffset recentThreshold = now - RecentlySeenWindow;

        List<Classified<T>> classified = candidates
            .Select(c =>
            {
                statesByQuestion.TryGetValue(questionId(c), out QuestionStudyState? state);
                bool recentlySeen = state is not null && state.LastSeenAt >= recentThreshold;
                return new Classified<T>(
                    c,
                    Priority(state),
                    recentlySeen,
                    state?.LastSeenAt ?? DateTimeOffset.MinValue);
            })
            .ToList();

        // «Свежие» (не недавно виденные) — по приоритетному бакету, шафл внутри бакета.
        var ordered = new List<T>(candidates.Count);
        foreach (IGrouping<int, Classified<T>> bucket in classified
                     .Where(x => !x.RecentlySeen)
                     .GroupBy(x => x.Bucket)
                     .OrderBy(g => g.Key))
        {
            ordered.AddRange(Shuffle(bucket.Select(x => x.Candidate).ToList()));
        }

        // Добор: если «свежих» не хватило на N — недавно виденные, least-recently-seen first.
        if (ordered.Count < count)
        {
            ordered.AddRange(classified
                .Where(x => x.RecentlySeen)
                .OrderBy(x => x.LastSeenAt)
                .Select(x => x.Candidate));
        }

        int take = Math.Min(count, ordered.Count); // == min(count, candidates.Count)
        return ordered.Take(take).ToList();
    }

    private static int Priority(QuestionStudyState? state) =>
        state is null
            ? PRIORITY_NEW_OR_WRONG
            : state.Status switch
            {
                StudyStatus.WRONG or StudyStatus.REVIEW => PRIORITY_NEW_OR_WRONG,
                StudyStatus.SEEN => PRIORITY_SEEN,
                StudyStatus.KNOWN => PRIORITY_KNOWN,
                _ => PRIORITY_SEEN,
            };

    /// <summary>Fisher–Yates shuffle через криптослучайность (без bias) — зеркалит старты сессий.</summary>
    private static List<T> Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private readonly record struct Classified<T>(T Candidate, int Bucket, bool RecentlySeen, DateTimeOffset LastSeenAt);
}
