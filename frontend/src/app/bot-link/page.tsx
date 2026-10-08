import type { Metadata } from "next";
import { BotLinkRedirect } from "./bot-link-redirect";
import { BotLinkProviders } from "./providers";

export const metadata: Metadata = {
  title: "Привязка Telegram",
};

// Страница использует useSession() через useMyProfile() и живёт вне `(app)`-группы,
// где находится <SessionProvider>. Без локального SessionProvider useSession() возвращает
// undefined → React error boundary показывает «Критическая ошибка». Оборачиваем в
// BotLinkProviders (mirror onboarding pattern: SessionProvider + QueryClient + SessionGuard
// + Toaster). Force-dynamic всё равно нужен — prerender тоже падает.
export const dynamic = "force-dynamic";

/**
 * `/bot-link` — one-click воронка для бота:
 *
 *   1. Юзер кликает «🔑 Войти и привязать» в боте → попадает сюда.
 *   2. Если не залогинен → SessionGuard redirect на `/login?next=/bot-link`.
 *   3. Залогинен + TG не привязан → автоматически генерится link-token и
 *      браузер редиректится на `t.me/<bot>?start=link_<token>`.
 *   4. Залогинен + TG уже привязан → шлёт обратно в `t.me/<bot>` (просто открывает чат).
 *
 * Минимизирует трение: 1 клик в боте → 1 клик «Войти» на платформе → бот привязан.
 */
export default function BotLinkPage() {
  return (
    <BotLinkProviders>
      <BotLinkRedirect />
    </BotLinkProviders>
  );
}
