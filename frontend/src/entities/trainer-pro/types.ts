/**
 * Контракты подписки «Тренажёр Pro» (#674) — зеркалят C# `AccessService.Contracts.TrainerPro`.
 * Тренажёр продаётся ОТДЕЛЬНО от платформенного каталога (`/pricing`): свои публичный оффер-эндпоинт
 * (`/access/trainer-pro/offer`), order-эндпоинт (`/access/trainer-pro/orders`) и admin-CRUD
 * (`/access/admin/trainer-pro/offer`). Под капотом каждый вариант — `Plan` (Scope=TRAINER,
 * OfferType=TRAINER_PRO, recurring term), но фронт работает с ним только через trainer-API.
 */

/**
 * Публичный покупаемый вариант подписки тренажёра (анонимный `GET /access/trainer-pro/offer`).
 * Зеркало `TrainerProOfferDto`. `priceCents`/`effectivePriceCents` — в копейках (могут быть null
 * у TBD-цены); `effectivePriceCents` учитывает активную акцию.
 */
export interface TrainerProOfferDto {
  id: string;
  slug: string;
  displayName: string;
  shortDescription: string;
  longDescription: string;
  coverFileId: string | null;
  features: string[];
  priceCents: number | null;
  currency: string;
  discountPercent: number | null;
  discountEndsAt: string | null;
  promotionActive: boolean;
  effectivePriceCents: number | null;
  /** Интервал автосписания в днях (период подписки); null для не-recurring. 30 → «/мес». */
  recurringIntervalDays: number | null;
  capabilities: string[];
  isHighlighted: boolean;
  displayOrder: number;
}

/**
 * Admin-проекция оффера (`GET /access/admin/trainer-pro/offer`, perm `plans.manage`). Зеркалит
 * публичный DTO + статус-флаги, чтобы автор видел и опубликованные, и черновые варианты.
 */
export interface TrainerProOfferAdminDto extends TrainerProOfferDto {
  discountStartsAt: string | null;
  isPublic: boolean;
  isActive: boolean;
  createdAt: string;
  archivedAt: string | null;
}

/**
 * Тело `POST /access/admin/trainer-pro/offer`. Бэкенд форсит tier=SUBSCRIPTION / OfferType=TRAINER_PRO /
 * Scope=TRAINER. `isActive=true` (default) → вариант сразу публикуется (становится покупаемым).
 */
export interface CreateTrainerProOfferRequest {
  slug: string;
  displayName: string;
  priceCents: number;
  recurringIntervalDays: number;
  shortDescription?: string | null;
  longDescription?: string | null;
  coverFileId?: string | null;
  features?: string[] | null;
  currency?: string | null;
  isHighlighted?: boolean | null;
  displayOrder?: number | null;
  isActive?: boolean | null;
}

/** Тело `PATCH /access/admin/trainer-pro/offer/{planId}`. Все поля опциональны: пропуск = не менять. */
export interface UpdateTrainerProOfferRequest {
  priceCents?: number | null;
  currency?: string | null;
  displayName?: string | null;
  shortDescription?: string | null;
  longDescription?: string | null;
  coverFileId?: string | null;
  features?: string[] | null;
  isHighlighted?: boolean | null;
  displayOrder?: number | null;
  /** Переключает покупаемость (publish/unpublish, не архив). */
  isActive?: boolean | null;
}

/** Тело `POST /access/trainer-pro/orders`. Цена снапшотится сервером из плана (анти-tamper). */
export interface CreateTrainerProOrderRequest {
  planId: string;
}

/** Ответ order-эндпоинта — `{orderId, paymentUrl}`. Фронт редиректит на `paymentUrl` (T-Bank). */
export interface CreateTrainerProOrderResponse {
  orderId: string;
  paymentUrl: string;
}
