export const GROWTH_EVENT_VERSION = "growth.v1" as const;
export const MAX_GROWTH_PROPERTY_LENGTH = 100;

export const GROWTH_EVENT_NAMES = [
  "landing_view",
  "cta_click",
  "catalog_view",
  "course_view",
  "free_material_open",
  "free_material_engaged",
  "level_test_started",
  "level_test_completed",
  "level_test_result_teaser",
  "level_test_auth_started",
  "level_test_claimed",
  "level_test_recommendation_clicked",
  "auth_started",
  "auth_completed",
  "plan_selected",
  "checkout_started",
  "purchase_success",
  "first_material_started",
  "first_material_completed",
  "first_issue_submitted",
  "continue_learning_click",
] as const;

export type GrowthEventName = (typeof GROWTH_EVENT_NAMES)[number];
export type GrowthPlacement =
  | "hero"
  | "header"
  | "footer"
  | "catalog"
  | "course"
  | "material"
  | "pricing"
  | "level_test"
  | "continue_learning"
  | "other";
export const GROWTH_CTA_PLACEMENTS = {
  hero_full_access: "hero",
  hero_program: "hero",
  program_courses: "course",
  final_courses: "footer",
  final_consultation: "footer",
  pricing_trial: "pricing",
  pricing_all: "pricing",
  level_test: "level_test",
  free_materials: "material",
  floating_consultation: "other",
  header_primary: "header",
} as const satisfies Record<string, GrowthPlacement>;
export type GrowthCtaId = keyof typeof GROWTH_CTA_PLACEMENTS;
export type GrowthAuthFlow = "login" | "registration" | "checkout" | "level_test" | "other";
export type FreeMaterialEngagement = "time_30s" | "scroll_50" | "video_started" | "video_completed";
export type GrowthEventSource = "client" | "server";
export type GrowthEventConsentPolicy = "analytics" | "necessary";

interface GrowthEventDefinition {
  trigger: string;
  owner: string;
  source: GrowthEventSource;
  consent: GrowthEventConsentPolicy;
}

export const GROWTH_EVENT_REGISTRY = {
  landing_view: {
    trigger: "Первый render публичного landing URL",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  cta_click: {
    trigger: "Клик по CTA из закрытого списка placements",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  catalog_view: {
    trigger: "Первый render каталога курсов, материалов или тарифов",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  course_view: {
    trigger: "Первый render страницы курса",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  free_material_open: {
    trigger: "Открытие публичного бесплатного материала",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  free_material_engaged: {
    trigger: "30 секунд, 50% scroll или подтверждённое video-взаимодействие",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_started: {
    trigger: "Анонимный или авторизованный пользователь начал level-test",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_completed: {
    trigger: "Level-test успешно отправлен",
    owner: "ProgressService + frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_result_teaser: {
    trigger: "Показан анонимный teaser результата level-test",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_auth_started: {
    trigger: "Клик по auth CTA из teaser результата",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_claimed: {
    trigger: "Анонимная попытка привязана после входа",
    owner: "ProgressService + frontend",
    source: "client",
    consent: "analytics",
  },
  level_test_recommendation_clicked: {
    trigger: "Клик по рекомендованному курсу в результате level-test",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  auth_started: {
    trigger: "Переход в OTP-auth из измеряемой воронки",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  auth_completed: {
    trigger: "OTP-auth завершён и исходный flow восстановлен",
    owner: "AuthService + frontend",
    source: "client",
    consent: "analytics",
  },
  plan_selected: {
    trigger: "Пользователь выбрал тариф",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  checkout_started: {
    trigger: "Перед созданием idempotent заказа",
    owner: "AccessService + frontend",
    source: "client",
    consent: "analytics",
  },
  purchase_success: {
    trigger: "Страница успеха получила server-confirmed статус PAID",
    owner: "AccessService + frontend",
    source: "client",
    consent: "analytics",
  },
  first_material_started: {
    trigger: "Первый старт материала внутри курса",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  first_material_completed: {
    trigger: "Первое завершение материала внутри курса",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  first_issue_submitted: {
    trigger: "Первая отправка задания на проверку",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
  continue_learning_click: {
    trigger: "Клик по Continue Learning",
    owner: "frontend",
    source: "client",
    consent: "analytics",
  },
} as const satisfies Record<GrowthEventName, GrowthEventDefinition>;

export interface GrowthEventPropertiesMap {
  landing_view: never;
  cta_click: { cta_id: GrowthCtaId; placement: GrowthPlacement };
  catalog_view: { catalog_kind?: "courses" | "knowledge_base" | "pricing" };
  course_view: { course_id: string };
  free_material_open: { material_id: string; course_id?: string };
  free_material_engaged: {
    material_id: string;
    course_id?: string;
    engagement: FreeMaterialEngagement;
  };
  level_test_started: { test_id?: string };
  level_test_completed: { test_id?: string };
  level_test_result_teaser: { test_id?: string; result_band?: string };
  level_test_auth_started: { test_id?: string };
  level_test_claimed: { test_id?: string; result_band?: string };
  level_test_recommendation_clicked: { test_id?: string; course_id: string };
  auth_started: {
    flow: GrowthAuthFlow;
    correlation_id?: string;
  };
  auth_completed: {
    flow?: GrowthAuthFlow;
    new_account?: boolean;
    correlation_id?: string;
  };
  plan_selected: {
    plan_id: string;
    placement?: GrowthPlacement;
    correlation_id?: string;
  };
  checkout_started: { plan_id: string; correlation_id?: string };
  purchase_success: { plan_id: string; correlation_id?: string };
  first_material_started: { material_id: string; course_id?: string };
  first_material_completed: { material_id: string; course_id?: string };
  first_issue_submitted: { issue_id: string; course_id?: string };
  continue_learning_click: {
    target_type: "course" | "material" | "issue";
    course_id?: string;
    content_id?: string | undefined;
  };
}

export type GrowthEvent = {
  [Name in GrowthEventName]: GrowthEventPropertiesMap[Name] extends never
    ? { name: Name; properties?: never }
    : { name: Name; properties: GrowthEventPropertiesMap[Name] };
}[GrowthEventName];

type GoalValue = string | boolean;

const STRING_KEYS: Record<GrowthEventName, readonly string[]> = {
  landing_view: [],
  cta_click: [],
  catalog_view: [],
  course_view: ["course_id"],
  free_material_open: ["material_id", "course_id"],
  free_material_engaged: ["material_id", "course_id"],
  level_test_started: ["test_id"],
  level_test_completed: ["test_id"],
  level_test_result_teaser: ["test_id", "result_band"],
  level_test_auth_started: ["test_id"],
  level_test_claimed: ["test_id", "result_band"],
  level_test_recommendation_clicked: ["test_id", "course_id"],
  auth_started: [],
  auth_completed: [],
  plan_selected: ["plan_id"],
  checkout_started: ["plan_id"],
  purchase_success: ["plan_id"],
  first_material_started: ["material_id", "course_id"],
  first_material_completed: ["material_id", "course_id"],
  first_issue_submitted: ["issue_id", "course_id"],
  continue_learning_click: ["course_id", "content_id"],
};

const ENUM_KEYS: Partial<Record<GrowthEventName, Record<string, readonly string[]>>> = {
  catalog_view: { catalog_kind: ["courses", "knowledge_base", "pricing"] },
  free_material_engaged: {
    engagement: ["time_30s", "scroll_50", "video_started", "video_completed"],
  },
  auth_started: { flow: ["login", "registration", "checkout", "level_test", "other"] },
  auth_completed: { flow: ["login", "registration", "checkout", "level_test", "other"] },
  plan_selected: {
    placement: [
      "hero",
      "header",
      "footer",
      "catalog",
      "course",
      "material",
      "pricing",
      "level_test",
      "continue_learning",
      "other",
    ],
  },
  continue_learning_click: { target_type: ["course", "material", "issue"] },
};

const CORRELATED_EVENTS: ReadonlySet<GrowthEventName> = new Set([
  "plan_selected",
  "auth_started",
  "auth_completed",
  "checkout_started",
  "purchase_success",
]);
const CANONICAL_UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

export function normalizeGrowthEventProperties(event: GrowthEvent): Record<string, GoalValue> {
  const raw: Record<string, unknown> | undefined = event.properties
    ? { ...event.properties }
    : undefined;
  if (!raw) return {};

  const result: Record<string, GoalValue> = {};
  for (const key of STRING_KEYS[event.name]) {
    const value = raw[key];
    if (typeof value === "string" && value.length > 0) {
      result[key] = value.slice(0, MAX_GROWTH_PROPERTY_LENGTH);
    }
  }

  for (const [key, values] of Object.entries(ENUM_KEYS[event.name] ?? {})) {
    const value = raw[key];
    if (typeof value === "string" && values.includes(value)) result[key] = value;
  }

  if (
    event.name === "cta_click" &&
    typeof raw.cta_id === "string" &&
    Object.hasOwn(GROWTH_CTA_PLACEMENTS, raw.cta_id)
  ) {
    const ctaId = raw.cta_id as GrowthCtaId;
    if (raw.placement === GROWTH_CTA_PLACEMENTS[ctaId]) {
      result.cta_id = ctaId;
      result.placement = GROWTH_CTA_PLACEMENTS[ctaId];
    }
  }

  if (event.name === "auth_completed" && typeof raw.new_account === "boolean") {
    result.new_account = raw.new_account;
  }

  if (
    CORRELATED_EVENTS.has(event.name) &&
    typeof raw.correlation_id === "string" &&
    CANONICAL_UUID_PATTERN.test(raw.correlation_id)
  ) {
    result.correlation_id = raw.correlation_id;
  }

  return result;
}
