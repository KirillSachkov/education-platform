"use client";

import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";

type CommentNavigationContextValue = {
  focusComment: (commentId: string) => void;
  highlightedCommentId: string | null;
  registerCommentRef: (commentId: string) => (element: HTMLElement | null) => void;
  /**
   * Список id ancestor'ов целевого deep-link коммента. Каждый `CommentCard`
   * чей id совпал — должен инициализироваться с раскрытым «Показать ответы»,
   * чтобы цепочка зарендерилась до того, как браузер прыгнет на target.
   * Массив, не Set — стабильная reference от useQuery, без allocate-per-render
   * в провайдере; lookup `.includes()` ok для ≤10 ids.
   */
  forceExpandIds: readonly string[];
};

const EMPTY_FORCE_EXPAND_IDS: readonly string[] = [];

const CommentNavigationContext =
  createContext<CommentNavigationContextValue | null>(null);

interface CommentNavigationProviderProps {
  children: ReactNode;
  /**
   * Опциональный id коммента, который надо подсветить и проскроллить в видимую
   * область, как только он замаунтится. Используется для URL deep-link'а
   * (`?focus=<commentId>`) — например, переход с виджета «Обсуждения» на главной
   * к конкретному ответу.
   *
   * Если коммент находится глубже в треде, он отрендерится только после того,
   * как пользователь раскроет «Показать ответы» — pending focus останется
   * активным и сработает на attach'е DOM-узла.
   */
  pendingFocusCommentId?: string | null;
  /**
   * Опционально: id'шники ancestor-комментов целевого deep-link target'а
   * (root → … → direct parent). Пробрасываются в context, чтобы каждый
   * `CommentCard` с matching id раскрылся при первом рендере и подгрузил
   * свои replies — иначе deep-link target никогда не попадёт в DOM.
   */
  forceExpandIds?: readonly string[] | null;
}

const CLICK_HIGHLIGHT_DURATION_MS = 1000;
const DEEPLINK_HIGHLIGHT_DURATION_MS = 4000;

export function CommentNavigationProvider({
  children,
  pendingFocusCommentId = null,
  forceExpandIds = null,
}: CommentNavigationProviderProps) {
  const forceExpandList = forceExpandIds ?? EMPTY_FORCE_EXPAND_IDS;
  const elementsRef = useRef(new Map<string, HTMLElement>());
  const highlightTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Pending deeplink target — clears после того, как ref зарегистрировался и
  // фокус сработал. Хранится в ref, чтобы не дёргать рендеры.
  const pendingDeeplinkRef = useRef<string | null>(pendingFocusCommentId);
  const [highlightedCommentId, setHighlightedCommentId] = useState<string | null>(
    null,
  );

  function scrollAndHighlight(
    commentId: string,
    element: HTMLElement,
    durationMs: number,
  ) {
    element.scrollIntoView({
      behavior: "smooth",
      block: "center",
    });

    if (highlightTimeoutRef.current) {
      clearTimeout(highlightTimeoutRef.current);
    }

    setHighlightedCommentId(null);

    requestAnimationFrame(() => {
      setHighlightedCommentId(commentId);
    });

    highlightTimeoutRef.current = setTimeout(() => {
      setHighlightedCommentId((current) =>
        current === commentId ? null : current,
      );
    }, durationMs);
  }

  // Если URL поменялся (новый deep-link в той же сессии) — обновить pending ref,
  // и если коммент уже в DOM, подсветить асинхронно (queueMicrotask, чтобы не
  // вызывать setState прямо из effect — React Compiler жалуется).
  useEffect(() => {
    pendingDeeplinkRef.current = pendingFocusCommentId;
    if (!pendingFocusCommentId) return;
    const element = elementsRef.current.get(pendingFocusCommentId);
    if (!element) return;
    queueMicrotask(() => {
      if (pendingDeeplinkRef.current !== pendingFocusCommentId) return;
      scrollAndHighlight(pendingFocusCommentId, element, DEEPLINK_HIGHLIGHT_DURATION_MS);
      pendingDeeplinkRef.current = null;
    });
  }, [pendingFocusCommentId]);

  function registerCommentRef(commentId: string) {
    return (element: HTMLElement | null) => {
      if (element) {
        elementsRef.current.set(commentId, element);
        // Если этот коммент — pending deep-link target, фокусим прямо сейчас.
        if (pendingDeeplinkRef.current === commentId) {
          scrollAndHighlight(commentId, element, DEEPLINK_HIGHLIGHT_DURATION_MS);
          pendingDeeplinkRef.current = null;
        }
        return;
      }

      elementsRef.current.delete(commentId);
    };
  }

  function focusComment(commentId: string) {
    const element = elementsRef.current.get(commentId);
    if (!element) {
      return;
    }
    scrollAndHighlight(commentId, element, CLICK_HIGHLIGHT_DURATION_MS);
  }

  const value: CommentNavigationContextValue = {
    focusComment,
    highlightedCommentId,
    registerCommentRef,
    forceExpandIds: forceExpandList,
  };

  return (
    <CommentNavigationContext.Provider value={value}>
      {children}
    </CommentNavigationContext.Provider>
  );
}

export function useCommentNavigation() {
  const context = useContext(CommentNavigationContext);

  if (!context) {
    throw new Error(
      "useCommentNavigation must be used within CommentNavigationProvider",
    );
  }

  return context;
}
