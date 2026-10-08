/**
 * Russian error message translations keyed by backend error code.
 *
 * After the backend localization effort, most errors already arrive in Russian.
 * This dictionary acts as an override layer — use it when the frontend needs
 * tighter control over wording or when an error code's backend message
 * contains technical detail unsuitable for display.
 *
 * To add English support: create `en.ts` with the same keys.
 */
export const ru: Record<string, string> = {
  // ── Auth ────────────────────────────────────────────────────
  "auth.email_taken": "Этот email уже используется",
  "auth.username_taken": "Это имя пользователя уже занято",
  "auth.invalid_credentials": "Неверный email или пароль",
  "auth.account_locked": "Аккаунт заблокирован",
  "auth.invalid_otp_code": "Неверный или просроченный код",
  "auth.email_not_confirmed": "Email не подтверждён",
  "auth.github_auth_failed": "Ошибка аутентификации через GitHub",
  "auth.github_email_required":
    "У GitHub аккаунта должен быть публичный email",
  "auth.external_login_info_missing": "Данные внешнего входа отсутствуют",
  "auth.password_already_set": "Пароль уже установлен",
  "auth.invalid_reset_token":
    "Ссылка для сброса пароля недействительна или просрочена",
  "auth.cannot_modify_self": "Нельзя изменить собственный аккаунт",
  "auth.github_not_linked": "GitHub аккаунт не привязан",
  "auth.user_not_found": "Пользователь не найден",
  "auth.invalid_username": "Имя пользователя содержит недопустимые символы",
  "auth.invalid_role": "Недопустимая роль",
  "auth.too_many_attempts": "Слишком много попыток, попробуйте позже",

  // ── Auth — password validation ─────────────────────────────
  "auth.password.too_short": "Пароль должен содержать минимум 8 символов",
  "auth.password.requires_uppercase":
    "Пароль должен содержать заглавную букву",
  "auth.password.requires_lowercase":
    "Пароль должен содержать строчную букву",
  "auth.password.requires_digit": "Пароль должен содержать цифру",
  "auth.password.requires_special": "Пароль должен содержать спецсимвол",

  // ── Access ─────────────────────────────────────────────────
  "access.denied": "Нет доступа к этому контенту",
  "access.not_authenticated": "Необходимо войти в систему",
  "access.insufficient_permissions":
    "Недостаточно прав для выполнения этого действия",

  // ── Тренажёр (#614 B2) ─────────────────────────────────────
  // PRO-гейт + квоты: даже если UI-замок обойдён, тост должен быть понятным
  // и подталкивать к подписке (CTA на /pricing рендерится отдельно у вызова).
  "trainer.pro.required":
    "Это доступно по подписке Trainer Pro. Оформите подписку на странице тарифов.",
  "trainer.topic.locked":
    "Тема доступна по подписке Trainer Pro. Оформите подписку на странице тарифов.",
  "trainer.quota.exceeded":
    "Достигнут лимит AI-проверок. Откройте больше с подпиской Trainer Pro или попробуйте позже.",
  "trainer.transcribe.too_long":
    "Запись слишком длинная — ответьте короче и попробуйте снова.",
  "trainer.transcribe.invalid_audio":
    "Не удалось распознать запись. Перезапишите ответ и попробуйте снова.",
};
