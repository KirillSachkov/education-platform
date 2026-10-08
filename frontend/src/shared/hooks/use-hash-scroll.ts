import { useEffect } from "react";

/**
 * Скроллит к элементу с `id === window.location.hash` после монтирования и на каждый
 * `hashchange`. Нативный browser anchor-scroll срабатывает ДО того, как ленивый контент
 * страницы (витрины планов, гриды курсов через react-query) догрузился и увеличил высоту —
 * поэтому нижние якоря промахиваются, и страница «открывается с начала» (#640).
 *
 * Хук перевыравнивается на нескольких чекпоинтах, пока высота не стабилизируется.
 * Discrete-таймеры (а не непрерывный rAF-loop) выбраны намеренно: они не дерутся с ручным
 * скроллом пользователя за пределами короткого окна догрузки. `scrollIntoView({block:"start"})`
 * уважает CSS `scroll-margin-top` (классы `scroll-mt-*` на якорях).
 *
 * @param enabled — обычно `!isLoading`: первый проход должен идти после того, как секции с
 *   якорями уже в DOM. Смена флага перезапускает эффект и выравнивание.
 */
export function useHashScroll(enabled = true) {
  useEffect(() => {
    if (!enabled || typeof window === "undefined") return;

    let timers: ReturnType<typeof setTimeout>[] = [];
    const clearTimers = () => {
      timers.forEach(clearTimeout);
      timers = [];
    };

    // Реальный жест пользователя отменяет дальнейшее до-выравнивание, чтобы на медленном
    // соединении нас не «выдёргивало» обратно к якорю. Слушаем именно intent-события
    // (wheel/touchmove/keydown), а НЕ `scroll` — иначе наш же `scrollIntoView` себя отменит.
    const userIntent = ["wheel", "touchmove", "keydown"] as const;
    const onUserIntent = () => clearTimers();

    const scrollToHash = () => {
      clearTimers();
      const id = decodeURIComponent(window.location.hash.replace(/^#/, ""));
      if (!id) return;

      const align = () => {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: "start" });
      };

      align();
      // Перевыравниваемся по мере догрузки контента (react-query → грид витрины растёт).
      timers = [80, 200, 400, 700].map((delay) => setTimeout(align, delay));
    };

    scrollToHash();
    window.addEventListener("hashchange", scrollToHash);
    userIntent.forEach((e) => window.addEventListener(e, onUserIntent, { passive: true }));

    return () => {
      window.removeEventListener("hashchange", scrollToHash);
      userIntent.forEach((e) => window.removeEventListener(e, onUserIntent));
      clearTimers();
    };
  }, [enabled]);
}
