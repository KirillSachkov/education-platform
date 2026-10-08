import type { AccessType } from "@/shared/config/access-type";
import type { CourseKind } from "@/shared/config/course-kind";
import type { ModuleItemDto } from "@/entities/module";

export type CourseId = string;
export type CourseViewTab = "modules" | "projects";

export type CourseStatus = "DRAFT" | "PUBLISHED" | "ARCHIVED";

export interface CourseSummaryDto {
  id: CourseId;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  status: CourseStatus;
  kind: CourseKind;
  imageId: string | null;
  videoId: string | null;
  isNew: boolean;
  sortKey: string;
  createdAt: string;
  updatedAt: string;
  /**
   * Display-флаг «входит в полный доступ» (issue #418). Управляет показом курса
   * в showcase на /pricing. На доступ не влияет. Отдаётся `/courses/my` и
   * `/courses/admin-list` (см. `Course.ShowInFullAccess`).
   */
  showInFullAccess: boolean;
  /**
   * Одобрен ли курс к показу в публичном каталоге (issue #569, model A — соавтор).
   * Автор всегда видит свой курс в `/courses/my` независимо от флага; `false` ⇒
   * UI рисует бейдж «на модерации витрины». Default `true` (обратная совместимость).
   */
  isCatalogListed: boolean;
  /**
   * Авторский кредит курса (#637) — обогащается бэком (`IAuthorLookupClient` +
   * FileService) для platform-wide списка «Курсы платформы», где admin видит курсы
   * разных авторов. `null`, если AuthService недоступен / нет аватара.
   */
  authorDisplayName?: string | null;
  authorAvatarUrl?: string | null;
}

/**
 * Author-defined drag-n-drop reorder of a course in the author's catalog.
 * Pass either `afterSortKey` (place after that course) or `beforeSortKey` (place before),
 * or both (place between). At least one must be set.
 */
export interface MoveCourseRequest {
  afterSortKey?: string;
  beforeSortKey?: string;
}

export interface CreateCourseRequest {
  title: string;
  description: string;
  slug: string;
  /** По умолчанию `COURSE`. Передаём `INTENSIVE` для интенсивов (без issues). */
  kind?: CourseKind;
}

/**
 * PATCH-семантика: указанные поля обновляются, отсутствующие — не трогаются.
 * Для очистки nullable-поля передавай `null` явно.
 *
 * @property gettingStartedModuleId — `null` сбрасывает стартовый модуль.
 * @property previewId — `null` детачит обложку (sync detach через FileService),
 *   значение — привязывает (idempotent через sync bind).
 * @property videoId — аналогично previewId для трейлера курса.
 */
export interface UpdateCourseRequest {
  title?: string;
  description?: string;
  gettingStartedModuleId?: string | null;
  slug?: string;
  previewId?: string | null;
  videoId?: string | null;
  /** «Чему вы научитесь» — список результатов обучения (лендинг курса). */
  learningOutcomes?: string[];
  /** «Для кого этот курс» — целевая аудитория. */
  targetAudience?: string[];
  /** «Что нужно знать заранее» — пререквизиты. */
  prerequisites?: string[];
  /**
   * Display-флаг «входит в полный доступ» (issue #418) — показывать ли курс в
   * showcase на /pricing. На доступ не влияет. Optional-PATCH: шлём только при
   * изменении автором.
   */
  showInFullAccess?: boolean;
}

export interface GetMyCoursesRequest {
  cursor?: string;
  limit: number;
  /** Опциональный фильтр по типу курса. */
  kind?: CourseKind;
}

export interface CourseItemDto {
  id: string;
  referenceId: string;
  itemType: string;
  sortKey: string;
  isOptional: boolean;
  title: string | null;
  status: string | null;
}

export interface CourseDetailDto {
  id: string;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  status: string;
  kind: CourseKind;
  imageId: string | null;
  videoId: string | null;
  createdAt: string;
  updatedAt: string;
  items: CourseItemDto[];
  /** Авторский кредit курса (#569). null если AuthService недоступен / нет аватара. */
  authorDisplayName: string | null;
  authorAvatarUrl: string | null;
}

export interface CreateCourseModuleRequest {
  title: string;
  description: string | null;
}

export interface CreateCourseProjectRequest {
  title: string;
  description: string;
  detailedDescription?: string;
  requiresGithubConnection?: boolean;
  requiresReviewApp?: boolean;
  isAutoReviewEnabled?: boolean;
}

export interface MoveCourseItemRequest {
  afterSortKey?: string;
  beforeSortKey?: string;
}

export interface CoursePricingBlock {
  planId: string;
  /** Обычная (list) цена в копейках. */
  priceCents: number;
  currency: string;
  planSlug: string;
  /** Цена с учётом активной акции; равна priceCents если акции нет. */
  effectivePriceCents: number;
  /** Процент скидки для бейджа «−N%»; null если акция не активна. */
  discountPercent: number | null;
  /** Конец окна акции (ISO); для хинта. null если не активна. */
  discountEndsAt: string | null;
  /** Активна ли акция прямо сейчас (вычислено на сервере). */
  promotionActive: boolean;
}

export interface CourseCatalogDto {
  id: string;
  slug: string;
  title: string;
  description: string;
  kind: CourseKind;
  imageId: string | null;
  imageUrl: string | null;
  /**
   * true — у курса есть хотя бы один опубликованный урок или задание с открытым
   * доступом (PUBLIC / REGISTERED). Issue #358: AccessType.FREE удалён,
   * FREE-контент коллапсирован в REGISTERED.
   */
  hasFreeContent: boolean;
  isNew: boolean;
  createdAt: string;
  pricing: CoursePricingBlock | null;
  /** true — у вызывающего уже есть доступ к курсу (роль/план/enrollment). Аноним → false. Issue #418. */
  isAccessible: boolean;
  /**
   * Display-флаг «входит в полный доступ» (issue #418). Default `true`. Курсы со
   * снятым флагом не показываются в showcase «Полный доступ» на /pricing
   * (автор снимает у интенсивов, дублирующихся внутри курсов). На доступ не влияет.
   */
  showInFullAccess: boolean;
  /** Авторский кредit карточки (#569). null если AuthService недоступен / нет аватара. */
  authorDisplayName: string | null;
  authorAvatarUrl: string | null;
}

/**
 * Курс, ожидающий одобрения к показу в публичном каталоге (issue #569, model A).
 * Возвращается `GET /courses/admin/pending-listing/` (permission `content.moderate`),
 * обогащён авторским кредитом, чтобы модератор видел, чей курс одобряет.
 */
export interface PendingCatalogCourseDto {
  id: string;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  kind: CourseKind;
  imageId: string | null;
  createdAt: string;
  authorDisplayName: string | null;
  authorAvatarUrl: string | null;
}

export interface GetCatalogRequest {
  cursor?: string;
  limit: number;
  search?: string;
  /** Опциональный фильтр по типу курса. */
  kind?: CourseKind;
}

export interface CourseLandingStatsDto {
  moduleCount: number;
  lessonCount: number;
  issueCount: number;
  /** DISTINCT PUBLISHED-тесты из модулей курса (#551). */
  quizCount: number;
}

export interface CourseLandingDto {
  id: string;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  status: string;
  kind: CourseKind;
  imageId: string | null;
  imageUrl: string | null;
  videoId: string | null;
  stats: CourseLandingStatsDto;
  /**
   * true — у курса есть хотя бы один опубликованный урок или задание с открытым
   * доступом (PUBLIC / REGISTERED). Используется чтобы решить, показывать ли
   * кнопку «Попробовать бесплатно». Issue #358: AccessType.FREE удалён.
   */
  hasFreeContent: boolean;
  isNew: boolean;
  createdAt: string;
  updatedAt: string;
  sections: CurriculumSectionDto[];
}

export interface CourseCurriculumDto {
  id: string;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  status: string;
  /** COURSE | INTENSIVE | MARATHON — управляет адаптивной hero-статистикой. */
  kind: CourseKind;
  imageId: string | null;
  imageUrl: string | null;
  gettingStartedModuleId: string | null;
  hasFreeContent: boolean;
  isNew: boolean;
  createdAt: string;
  updatedAt: string;
  sections: CurriculumSectionDto[];
  /**
   * Author-authored landing copy (рендерится на странице курса не-купившим).
   * Optional — отсутствуют у незаполненных курсов и в ~3-мин окне после деплоя
   * (старый закешированный curriculum-JSON их не несёт).
   */
  learningOutcomes?: string[];
  targetAudience?: string[];
  prerequisites?: string[];
  /**
   * PUBLISHED-подборки курса — блок «Подборки» на странице программы и в
   * курс-сайдбаре (#508). Optional по той же причине, что learningOutcomes
   * (старый закешированный curriculum-JSON в ~3-мин окне после деплоя).
   */
  collections?: CurriculumCollectionDto[];
  /**
   * Подпись автора курса (#569) — имя + URL аватара для hero публичной страницы
   * курса. Optional/nullable: бэк отдаёт null, если AuthService недоступен / нет аватара,
   * и старый закешированный curriculum-JSON в ~3-мин окне после деплоя их не несёт.
   */
  authorDisplayName?: string | null;
  authorAvatarUrl?: string | null;
}

export interface CurriculumSectionDto {
  id: string;
  itemType: string;
  title: string;
  description: string | null;
  detailedDescription: string | null;
  sortKey: string;
  isOptional: boolean;
  items: CurriculumItemDto[];
}

export interface CurriculumItemDto {
  id: string;
  itemType: string;
  title: string;
  sortKey: string;
  isOptional: boolean;
  position: number;
  accessType: AccessType | null;
  viewPriority: string | null;
  materialKind: string | null;
  durationSeconds: number | null;
  coverUrl: string | null;
  /** Только для `itemType="Quiz"` (ST-12 #492): число вопросов PUBLISHED-квиза. */
  questionsCount?: number | null;
  /** Только для `itemType="Quiz"`: id квиза (совпадает с `id` — reference_id module_item'а). */
  quizId?: string | null;
}

/**
 * Опубликованная подборка курса в программе (#508). `itemsCount`/`materialIds`
 * считают только PUBLISHED-материалы — зеркалят знаменатели прогресс-blueprint'а
 * (#496), поэтому «X из Y» по подборке сходится с общим счётчиком курса.
 * X считается локально: materialIds ∩ learning-state materials (VIEWED).
 */
export interface CurriculumCollectionDto {
  id: string;
  title: string;
  description: string | null;
  accessType: AccessType;
  itemsCount: number;
  materialIds: string[];
  coverUrl: string | null;
}

export interface CourseBuilderDto {
  id: string;
  authorId: string;
  slug: string;
  title: string;
  description: string;
  status: string;
  kind: CourseKind;
  imageId: string | null;
  videoId: string | null;
  gettingStartedModuleId: string | null;
  createdAt: string;
  updatedAt: string;
  isNew: boolean;
  sections: BuilderSectionDto[];
  /** Author landing copy — редактируется в «Лендинг курса». */
  learningOutcomes?: string[];
  targetAudience?: string[];
  prerequisites?: string[];
}

export interface BuilderSectionDto {
  id: string;
  itemType: string;
  title: string;
  description: string | null;
  detailedDescription: string | null;
  status: string;
  sortKey: string;
  isOptional: boolean;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
  isAutoReviewEnabled: boolean;
  items: ModuleItemDto[];
}

/** Счётчики опубликованного контента платформы (стат-блок «Полный доступ», #437). */
export interface PlatformContentStatsDto {
  coursesCount: number;
  collectionsCount: number;
  materialsCount: number;
  issuesCount: number;
}

/** Передача курса другому автору — админ/модератор (#587). */
export interface ReassignCourseAuthorRequest {
  courseId: CourseId;
  newAuthorId: string;
}

export interface ReassignCourseAuthorResponse {
  courseId: string;
  newAuthorId: string;
  /** Материалы, оставшиеся у прежнего автора (привязаны и к другим курсам). */
  skippedSharedMaterialIds: string[];
  /** Тесты, оставшиеся у прежнего автора (привязаны и к другим курсам). */
  skippedSharedQuizIds: string[];
}
