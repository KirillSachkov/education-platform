import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { cn } from "@/shared/lib/css";
import Link from "next/link";

function getInitial(name: string): string {
  return name.trim().charAt(0).toUpperCase() || "?";
}

export interface AuthorCreditProps {
  /** Имя автора. Если `null`/пусто — компонент ничего не рендерит. */
  name: string | null | undefined;
  /** Готовый URL аватара (бэк отдаёт абсолютный/относительный). null → инициал. */
  avatarUrl?: string | null;
  className?: string;
  /** Размер аватара. `sm` — компактные карточки, `md` — страницы детали. */
  size?: "sm" | "md";
  /**
   * Опциональная ссылка на профиль автора (`/users/{id}`, #637). Если задана —
   * кредит становится кликабельным (аватар + имя ведут на профиль) с hover/focus
   * affordance. Не передавай `href`, когда кредит уже вложен в другую `<a>`
   * (карточка-ссылка) — вложенные анкоры невалидны.
   */
  href?: string;
}

/**
 * Подпись автора контента (#569, model A — соавтор в общей витрине): аватар +
 * имя. С `href` (#637) — кликабельная ссылка на профиль автора, без — чистый
 * кредит. Рендерит `null`, если имя не задано (бэк не смог резолвнуть автора
 * через AuthService).
 */
export function AuthorCredit({ name, avatarUrl, className, size = "sm", href }: AuthorCreditProps) {
  if (!name) return null;

  const avatarSize = size === "md" ? "size-6" : "size-5";
  const textSize = size === "md" ? "text-sm" : "text-xs";

  const inner = (
    <>
      <Avatar className={cn(avatarSize, "shrink-0")}>
        {avatarUrl && <AvatarImage src={avatarUrl} alt="" />}
        <AvatarFallback className="bg-gradient-primary text-[9px] font-bold text-primary-foreground">
          {getInitial(name)}
        </AvatarFallback>
      </Avatar>
      <span className={cn("truncate", textSize)}>{name}</span>
    </>
  );

  const baseClass = "inline-flex min-w-0 items-center gap-1.5 text-muted-foreground";

  if (href) {
    return (
      <Link
        href={href}
        className={cn(
          baseClass,
          "rounded-full transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:ring-offset-background [&>span]:hover:underline [&>span]:underline-offset-2",
          className,
        )}
      >
        {inner}
      </Link>
    );
  }

  return <span className={cn(baseClass, className)}>{inner}</span>;
}
