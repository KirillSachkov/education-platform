export type MaterialKind = "ARTICLE" | "VIDEO" | "NOTE" | "STREAM";

export type MaterialStatus = "DRAFT" | "PUBLISHED" | "ARCHIVED";

export type MaterialAccessType = "PUBLIC" | "REGISTERED" | "ENROLLED";

export type MaterialScope = "mine" | "public" | "private";

export type MaterialId = string;

export interface MaterialVideoDto {
  externalVideoId: string | null;
  thumbnailUrl: string | null;
  durationSeconds: number | null;
  status: string | null;
}

export interface MaterialSummaryDto {
  id: MaterialId;
  authorId: string;
  title: string;
  preview: string | null;
  kind: MaterialKind;
  status: MaterialStatus;
  accessType: MaterialAccessType;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  imageId?: string | null;
  videoId?: string | null;
  thumbnailUrl?: string | null;
  /**
   * Курсы, к которым материал привязан через `course_materials`. Picker модуля/коллекции
   * показывает badge «В курсе: <title>», чтобы автор видел источник материала при
   * переиспользовании. Пустой массив или `undefined` — материал в базе знаний.
   * Заполняется бэкендом только для scope=mine.
   */
  courses?: MaterialCourseBindingDto[];
  /**
   * Уникальные просмотры (auth users + анонимы по cookie). Обогащается ECS из
   * ProgressService. Optional — стабильность к старым React Query кешам. Issue #234.
   */
  viewsCount?: number;
  /**
   * Длительность привязанного видео в секундах (Kinescope). null/undefined — материал
   * без видео или метаданные ещё не готовы. Issue #500.
   */
  durationSeconds?: number | null;
  /**
   * Авторский кредit карточки (#569). Optional — не все surface'ы обогащают автора.
   */
  authorDisplayName?: string | null;
  authorAvatarUrl?: string | null;
}

export type MaterialLockReason =
  | "anonymous"
  // Legacy course-trial — LockReasonResolver всё ещё эмитит это значение на
  // course:X:trial тегах (search-выдача мапит его в MaterialFeedItemDto).
  | "trial_required"
  | "not_enrolled"
  | "standard_required"
  | "plan_required";

export interface MaterialFeedItemDto {
  id: MaterialId;
  authorId: string;
  title: string;
  preview: string | null;
  kind: MaterialKind;
  status: MaterialStatus;
  accessType: MaterialAccessType;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  imageId: string | null;
  videoId: string | null;
  thumbnailUrl: string | null;
  moduleTitle: string | null;
  courseId: string | null;
  courseTitle: string | null;
  courseSlug: string | null;
  isAccessible: boolean;
  lockReason: MaterialLockReason | null;
  /**
   * Уникальные просмотры (auth users + анонимы по cookie). Issue #234.
   */
  viewsCount?: number;
  /**
   * Длительность привязанного видео в секундах (Kinescope). Issue #500.
   */
  durationSeconds?: number | null;
  /**
   * Авторский кредit карточки (#569). Мёржится из card-meta для базы знаний.
   */
  authorDisplayName?: string | null;
  authorAvatarUrl?: string | null;
}

export type MaterialFeedScope = "all" | "enrolled";

export interface MaterialCourseBindingDto {
  courseId: string;
  title: string;
  slug: string;
  authorId: string;
}

export interface MaterialModuleBindingDto {
  moduleId: string;
  title: string;
  courseId: string;
  courseTitle: string;
  courseSlug: string;
}

export interface MaterialCollectionBindingDto {
  collectionId: string;
  title: string;
  courseId: string | null;
  courseSlug: string | null;
}

export interface MaterialBindingsDto {
  materialId: MaterialId;
  courses: MaterialCourseBindingDto[];
  modules: MaterialModuleBindingDto[];
  collections: MaterialCollectionBindingDto[];
}

/**
 * Лёгкая мета карточки: просмотры + длительность видео. Батч-эндпоинт для поверхностей,
 * которые строят карточки не из ECS-фидов (база знаний — из search-документов). Issue #500.
 */
export interface MaterialCardMetaDto {
  materialId: MaterialId;
  viewsCount: number;
  durationSeconds: number | null;
  /** Авторский кредit карточки (#569). null если AuthService недоступен / нет аватара. */
  authorDisplayName: string | null;
  authorAvatarUrl: string | null;
}

/**
 * Глава видео-материала — заголовок + offset в секундах от начала видео.
 * Денормализуется из `Material.ChapterTitles[]` + `ChapterTimestamps[]` на бэке.
 * Список отсортирован по `timeSeconds`.
 */
export interface MaterialChapterDto {
  title: string;
  timeSeconds: number;
}

export interface MaterialDetailDto {
  id: MaterialId;
  authorId: string;
  title: string;
  content: string | null;
  /**
   * Авторское «Описание» — полезные ссылки и связанные материалы для урока
   * (markdown). Отдельно от `content` (AI-конспект). Optional — стабильность
   * к старым React Query кешам, заполненным до деплоя.
   */
  description?: string | null;
  kind: MaterialKind;
  status: MaterialStatus;
  accessType: MaterialAccessType;
  isAccessible: boolean;
  imageId: string | null;
  imageUrl: string | null;
  videoId: string | null;
  video: MaterialVideoDto | null;
  createdAt: string;
  updatedAt: string;
  /**
   * Количество курсов, к которым привязан материал через `course_materials`.
   * Используется для подсказок селектора AccessType (course-bound vs orphan).
   */
  courseCount: number;
  /**
   * Главы видео (отсортированы по offset). Пустой массив для не-видео и видео без таймкодов.
   * Optional — стабильность к React Query кешам, заполненным до деплоя, когда поле ещё не возвращалось.
   */
  chapters?: MaterialChapterDto[];
  /**
   * Уникальные просмотры (auth users + анонимы по cookie). Optional — стабильность
   * к старым кешам. Issue #234.
   */
  viewsCount?: number;
  /**
   * Привязанный квиз «Проверь себя» (`materials.quiz_id`, #489). Квиз — standalone
   * сущность: один квиз может переиспользоваться несколькими материалами.
   * Optional — стабильность к старым React Query кешам.
   */
  quizId?: string | null;
  /**
   * Авторский кредit материала (#569). null если AuthService недоступен / нет аватара.
   * Optional — стабильность к старым React Query кешам.
   */
  authorDisplayName?: string | null;
  authorAvatarUrl?: string | null;
}

/**
 * Запрос на создание draft-материала (YouTube-style: entity создаётся сразу при
 * открытии формы, чтобы refresh не терял прогресс).
 */
export interface CreateDraftMaterialRequest {
  /** Опциональная привязка к курсу — материал сразу попадает в course_materials. */
  courseId?: string;
  /** Опциональная привязка к модулю — материал попадает в module_items с ViewPriority=Key. */
  moduleId?: string;
  /**
   * Опциональная привязка к подборке. Оба поля (`collectionId` + `sectionId`) либо
   * заданы, либо `undefined`. При наличии — материал атомарно добавляется в
   * `collection_items` секции в той же транзакции.
   */
  collectionId?: string;
  sectionId?: string;
}

export interface CreateMaterialRequest {
  title: string;
  content: string | null;
  /** Авторское «Описание» (markdown) — полезные ссылки и материалы для урока. */
  description?: string | null;
  kind: MaterialKind;
  accessType: MaterialAccessType;
  draftId?: string;
  /**
   * Идентификатор уже загруженного видео-ассета. Бэкенд синхронно
   * привязывает его к материалу до первого save — на ошибке материал
   * не создаётся.
   */
  videoId?: string;
  /** Идентификатор уже загруженной обложки. Поведение аналогично `videoId`. */
  previewId?: string;
  /** Опциональный id курса — если задан, материал атомарно привязывается к course_materials. */
  courseId?: string;
  /**
   * Опциональный id модуля — если задан, материал атомарно добавляется в module_items.
   * Автоматически также создаёт course_materials для курса этого модуля, если `courseId` не передан.
   */
  moduleId?: string;
  /** Привязка квиза «Проверь себя» при создании (квиз должен принадлежать caller'у). */
  quizId?: string;
  /**
   * Если `true`, бэкенд переводит материал в `PUBLISHED` в той же транзакции
   * и публикует `MaterialPublished` event. Требует наличия `content` или `videoId`.
   */
  publishOnCreate?: boolean;
  /**
   * Применяется только при `publishOnCreate=true`. Управляет рассылкой уведомления
   * подписчикам курса о новом материале. Default: true (уведомить).
   */
  notifySubscribers?: boolean;
}

export interface UpdateMaterialRequest {
  title: string;
  content: string | null;
  /** Авторское «Описание» (markdown) — полезные ссылки и материалы для урока. */
  description?: string | null;
  kind: MaterialKind;
  accessType: MaterialAccessType;
  /**
   * Желаемое состояние привязки видео. `null` ⇒ открепить текущее (sync detach
   * через FileService). Значение ⇒ привязать (idempotent).
   */
  videoId?: string | null;
  /** Аналогично `videoId` для обложки. */
  previewId?: string | null;
  /**
   * Желаемое состояние ссылки на квиз «Проверь себя» (#489). PUT-семантика:
   * `null`/отсутствие ⇒ отвязать (сам квиз продолжает жить — он standalone),
   * значение ⇒ привязать. ВАЖНО: каждый update-путь обязан передавать текущий
   * quizId, иначе привязка слетит.
   */
  quizId?: string | null;
}

export interface GetMaterialsRequest {
  scope?: MaterialScope;
  cursor?: string;
  limit?: number;
  kind?: MaterialKind;
  authorId?: string;
  search?: string;
  /** `"free"` — отсечь ENROLLED, оставить только PUBLIC/REGISTERED. */
  accessFilter?: "free";
}
