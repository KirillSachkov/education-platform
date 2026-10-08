"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import {
  useMarkAllAsRead,
  useMarkAsRead,
  useNotifications,
  useUnreadCount,
} from "@/features/notifications";
import {
  NotificationCategories,
  NotificationCategoryLabels,
  NotificationDetailDialog,
  NotificationTypes,
  categoryOf,
  notificationHref,
  typesInCategory,
  type Notification,
  type NotificationCategory,
  type NotificationType,
} from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { formatRelativeDate, formatShortDate } from "@/shared/lib/date";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Tabs, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { Icons, type IconComponent } from "@/shared/ui/icons";

/**
 * Полная страница «Центр уведомлений» — `/notifications`.
 *
 * Layout: type-icon chip → title/body → действия. Группировка по дате (сегодня/вчера/
 * на этой неделе/раньше). Unread выделяется фоном и точкой. Hover-действие
 * «Прочитать» появляется на десктопе.
 */
export function NotificationsCenter() {
  // SSE-стрим уже держит NotificationBell в AppLayout (он всегда смонтирован).
  // Дублировать `useNotificationStream()` нельзя: каждый вызов поднимает свой EventSource,
  // а на бэке concurrency-лимит на user — 3 SSE; вторая вкладка ловит 429.
  // Инвалидации queryClient'а из bell'a достаточно — этот же queryClient используется здесь.

  const [unreadOnly, setUnreadOnly] = useState(false);
  const [category, setCategory] = useState<NotificationCategory | "all">("all");
  // Один диалог на страницу (не per-row): уведомление без targetUrl
  // открывает полный текст здесь (#708).
  const [detail, setDetail] = useState<Notification | null>(null);
  const { count: unreadCount } = useUnreadCount();
  const { markAllAsRead, isPending: isMarkingAll } = useMarkAllAsRead();

  const types = category === "all" ? undefined : typesInCategory(category);

  const { items, hasNextPage, fetchNextPage, isLoading, isFetchingNextPage, error, refetch } =
    useNotifications({
      limit: 30,
      unreadOnly: unreadOnly || undefined,
      types,
    });

  const grouped = groupByDate(items);

  const categoryTabs: { value: NotificationCategory | "all"; label: string }[] = [
    { value: "all", label: "Все" },
    {
      value: NotificationCategories.Course,
      label: NotificationCategoryLabels[NotificationCategories.Course],
    },
    {
      value: NotificationCategories.Submissions,
      label: NotificationCategoryLabels[NotificationCategories.Submissions],
    },
    {
      value: NotificationCategories.Comments,
      label: NotificationCategoryLabels[NotificationCategories.Comments],
    },
    {
      value: NotificationCategories.Account,
      label: NotificationCategoryLabels[NotificationCategories.Account],
    },
  ];

  return (
    <div className="max-w-3xl mx-auto px-4 sm:px-6 py-6 sm:py-10 space-y-7">
      <header className="space-y-3">
        <div className="flex items-start justify-between gap-3">
          <h1 className="text-2xl sm:text-3xl font-semibold tracking-tight">Уведомления</h1>
          {unreadCount > 0 && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => markAllAsRead()}
              disabled={isMarkingAll}
              className="-mr-2 text-muted-foreground hover:text-foreground"
            >
              <Icons.checkAll size={14} className="mr-1.5" />
              Прочитать все
            </Button>
          )}
        </div>

        <div className="flex items-center gap-3 flex-wrap">
          <p className="text-sm text-muted-foreground">
            {unreadCount > 0 ? `Непрочитанных: ${unreadCount}` : "Все прочитаны"}
          </p>
          {unreadCount > 0 && (
            <button
              type="button"
              onClick={() => setUnreadOnly(!unreadOnly)}
              className={cn(
                "text-xs font-medium transition-colors",
                "border rounded-full px-2.5 h-6 inline-flex items-center",
                unreadOnly
                  ? "bg-primary/10 border-primary/30 text-primary"
                  : "border-border/60 text-muted-foreground hover:text-foreground hover:border-border",
              )}
            >
              Только непрочитанные
            </button>
          )}
        </div>
      </header>

      <ScrollableTabs
        value={category}
        onValueChange={(v) => setCategory(v as NotificationCategory | "all")}
      >
        {categoryTabs.map((tab) => (
          <TabsTrigger key={tab.value} value={tab.value}>
            {tab.label}
          </TabsTrigger>
        ))}
      </ScrollableTabs>

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Icons.loading size={20} className="animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <div className="flex flex-col items-center justify-center py-20 text-center gap-3">
          <Icons.error size={20} className="text-destructive" />
          <p className="text-sm text-muted-foreground">
            {getErrorMessage(error, "Не удалось загрузить уведомления")}
          </p>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            Повторить
          </Button>
        </div>
      ) : items.length === 0 ? (
        <EmptyInbox unreadOnly={unreadOnly} />
      ) : (
        <div className="space-y-7">
          {grouped.map(({ key, label, list }) => (
            <section key={key} className="space-y-1.5">
              <h2 className="px-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground/80">
                {label}
              </h2>
              <ul className="space-y-1 -mx-1.5">
                {list.map((n) => (
                  <li
                    key={n.id}
                    // Skip rendering offscreen notifications until they scroll into the viewport.
                    // `contain-intrinsic-size` reserves a placeholder height so the scrollbar
                    // stays stable while items lazily upgrade.
                    className="[content-visibility:auto] [contain-intrinsic-size:auto_72px]"
                  >
                    <NotificationRow notification={n} onOpenDetail={setDetail} />
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}

      <NotificationDetailDialog
        notification={detail}
        onOpenChange={(open) => {
          if (!open) setDetail(null);
        }}
      />

      {hasNextPage && !isLoading && items.length > 0 && (
        <div className="flex justify-center pt-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
            className="text-muted-foreground"
          >
            {isFetchingNextPage ? (
              <>
                <Icons.loading size={13} className="animate-spin mr-2" />
                Загружаем…
              </>
            ) : (
              "Показать ещё"
            )}
          </Button>
        </div>
      )}
    </div>
  );
}

/* ───────────────────────── Empty state ───────────────────────── */

function EmptyInbox({ unreadOnly }: { unreadOnly: boolean }) {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center gap-3">
      <span className="flex size-14 items-center justify-center rounded-2xl bg-muted/50 text-muted-foreground/60">
        <Icons.notification size={22} />
      </span>
      <div className="space-y-0.5">
        <p className="text-sm font-medium text-foreground">
          {unreadOnly ? "Все прочитаны" : "Уведомлений пока нет"}
        </p>
        <p className="text-xs text-muted-foreground max-w-xs">
          {unreadOnly
            ? "Здесь появятся новые уведомления, когда они придут."
            : "Сюда приходят ответы на ревью, комментарии и обновления курсов."}
        </p>
      </div>
    </div>
  );
}

/* ───────────────────────── Tabs scroll wrapper ───────────────────────── */

function ScrollableTabs({
  value,
  onValueChange,
  children,
  className,
}: {
  value: string;
  onValueChange: (v: string) => void;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "overflow-x-auto -mx-1 px-1 [&::-webkit-scrollbar]:hidden [scrollbar-width:none]",
        className,
      )}
    >
      <Tabs value={value} onValueChange={onValueChange}>
        <TabsList>{children}</TabsList>
      </Tabs>
    </div>
  );
}

/* ───────────────────────── Row ───────────────────────── */

function NotificationRow({
  notification,
  onOpenDetail,
}: {
  notification: Notification;
  onOpenDetail: (notification: Notification) => void;
}) {
  const router = useRouter();
  const { markAsRead } = useMarkAsRead();
  const isUnread = !notification.readAt;
  const href = notificationHref(notification);
  const meta = typeMeta(notification.type);

  const handleClick = () => {
    if (isUnread) markAsRead(notification.id);
    // Нет deep-link'а → полный текст в модалке, а не редирект на главную (#708).
    if (href) router.push(href);
    else onOpenDetail(notification);
  };

  const handleMarkRead = (event: React.MouseEvent) => {
    event.stopPropagation();
    if (isUnread) markAsRead(notification.id);
  };

  return (
    <div
      className={cn(
        "group relative flex items-start gap-3 px-3 py-3 rounded-xl transition-colors",
        isUnread ? "bg-primary/[0.06] hover:bg-primary/10" : "hover:bg-muted/40",
      )}
    >
      {/* Клик всегда осмыслен: навигация по ссылке или модалка с полным текстом. */}
      <button
        type="button"
        onClick={handleClick}
        className={cn(
          "flex flex-1 min-w-0 items-start gap-3 text-left",
          "focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring rounded-lg",
        )}
      >
        <span
          className={cn(
            "flex size-9 shrink-0 items-center justify-center rounded-xl",
            meta.iconClass,
          )}
          aria-hidden
        >
          <meta.Icon className="size-4" />
        </span>

        <div className="min-w-0 flex-1">
          <div className="flex items-start gap-2">
            <p
              className={cn(
                "text-sm leading-snug line-clamp-2 flex-1 wrap-anywhere",
                isUnread ? "font-semibold text-foreground" : "font-medium text-foreground/80",
              )}
            >
              {notification.title}
            </p>
            {isUnread && (
              <span
                className="mt-1.5 size-2 rounded-full bg-primary shrink-0"
                aria-label="Непрочитано"
              />
            )}
          </div>
          {notification.body && (
            <p
              className={cn(
                "mt-1 text-sm line-clamp-2",
                isUnread ? "text-foreground/70" : "text-muted-foreground",
              )}
            >
              {notification.body}
            </p>
          )}
          <div className="mt-1.5 flex items-center gap-2 text-xs text-muted-foreground/70">
            <span>{meta.label}</span>
            <span aria-hidden>·</span>
            <time dateTime={notification.createdAt}>
              {formatRelativeDate(notification.createdAt)}
            </time>
          </div>
        </div>
      </button>

      {isUnread && (
        <button
          type="button"
          onClick={handleMarkRead}
          aria-label="Отметить как прочитанное"
          className={cn(
            "hidden md:inline-flex items-center self-center shrink-0",
            "text-xs font-medium text-muted-foreground hover:text-foreground",
            "rounded-md px-2 py-1 hover:bg-background/60",
            "opacity-0 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity",
            "focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
          )}
        >
          <Icons.check className="size-3.5 mr-1" />
          Прочитать
        </button>
      )}
    </div>
  );
}

/* ───────────────────────── Type → icon/colour ───────────────────────── */

type TypeMeta = {
  Icon: IconComponent;
  iconClass: string;
  label: string;
};

const TYPE_META: Record<NotificationType, TypeMeta> = {
  [NotificationTypes.Welcome]: {
    Icon: Icons.shield,
    iconClass: "bg-muted/60 text-muted-foreground",
    label: "Платформа",
  },
  [NotificationTypes.CourseEnrolled]: {
    Icon: Icons.course,
    iconClass: "bg-primary/10 text-primary",
    label: "Курс",
  },
  [NotificationTypes.MaterialPublished]: {
    Icon: Icons.document,
    iconClass: "bg-primary/10 text-primary",
    label: "Материал",
  },
  [NotificationTypes.IssueCreated]: {
    Icon: Icons.issue,
    iconClass: "bg-orange-dim text-orange",
    label: "Задача",
  },
  [NotificationTypes.IssueSubmissionApproved]: {
    Icon: Icons.completed,
    iconClass: "bg-green-dim text-green",
    label: "Решение принято",
  },
  [NotificationTypes.IssueSubmissionChangesRequested]: {
    Icon: Icons.reviewChangesRequested,
    iconClass: "bg-orange-dim text-orange",
    label: "Нужны правки",
  },
  [NotificationTypes.IssueSubmissionAwaitingReview]: {
    Icon: Icons.reviewSubmitted,
    iconClass: "bg-purple-dim text-purple",
    label: "Ревью",
  },
  [NotificationTypes.AuthorAnnouncement]: {
    Icon: Icons.notification,
    iconClass: "bg-primary/10 text-primary",
    label: "Объявление",
  },
  [NotificationTypes.TelegramLinked]: {
    Icon: Icons.telegram,
    iconClass: "bg-blue-dim text-blue",
    label: "Telegram",
  },
  [NotificationTypes.CommentReplied]: {
    Icon: Icons.message,
    iconClass: "bg-blue-dim text-blue",
    label: "Ответ",
  },
  [NotificationTypes.CommentOnOwnContent]: {
    Icon: Icons.comment,
    iconClass: "bg-blue-dim text-blue",
    label: "Комментарий",
  },
  [NotificationTypes.IssuePublished]: {
    Icon: Icons.issue,
    iconClass: "bg-orange-dim text-orange",
    label: "Задание",
  },
  [NotificationTypes.PlanGrantReceived]: {
    Icon: Icons.completed,
    iconClass: "bg-green-dim text-green",
    label: "Доступ",
  },
  [NotificationTypes.AuthorHelpRequested]: {
    Icon: Icons.reviewSubmitted,
    iconClass: "bg-purple-dim text-purple",
    label: "Нужна помощь",
  },
  [NotificationTypes.PlanGrantAuthorSale]: {
    Icon: Icons.creditCard,
    iconClass: "bg-green-dim text-green",
    label: "Продажа",
  },
  [NotificationTypes.WeeklyDigest]: {
    Icon: Icons.calendar,
    iconClass: "bg-primary/10 text-primary",
    label: "Дайджест",
  },
  [NotificationTypes.AiReviewOversizedSkipped]: {
    Icon: Icons.warning,
    iconClass: "bg-orange-dim text-orange",
    label: "Большой PR",
  },
  [NotificationTypes.UserLeveledUp]: {
    Icon: Icons.trophy,
    iconClass: "bg-amber-500/15 text-amber-500",
    label: "Новый уровень",
  },
  [NotificationTypes.LevelTestInvite]: {
    Icon: Icons.target,
    iconClass: "bg-primary/10 text-primary",
    label: "Тест уровня",
  },
  [NotificationTypes.TelegramJoinReminder]: {
    Icon: Icons.telegram,
    iconClass: "bg-blue-dim text-blue",
    label: "Telegram-группа",
  },
  [NotificationTypes.VideoAutoProcessingFailed]: {
    Icon: Icons.warning,
    iconClass: "bg-orange-dim text-orange",
    label: "Обработка видео",
  },
  [NotificationTypes.AccessExpired]: {
    Icon: Icons.warning,
    iconClass: "bg-orange-dim text-orange",
    label: "Доступ истёк",
  },
  [NotificationTypes.EmailLoginNotice]: {
    Icon: Icons.mail,
    iconClass: "bg-blue-dim text-blue",
    label: "Вход по почте",
  },
  [NotificationTypes.LinkAccountsNudge]: {
    Icon: Icons.link,
    iconClass: "bg-primary/10 text-primary",
    label: "Привязка аккаунтов",
  },
  [NotificationTypes.StudentPrQuestionAsked]: {
    Icon: Icons.message,
    iconClass: "bg-purple-dim text-purple",
    label: "Вопрос по PR",
  },
};

function typeMeta(type: NotificationType): TypeMeta {
  return (
    TYPE_META[type] ?? {
      Icon: Icons.notification,
      iconClass: "bg-muted/60 text-muted-foreground",
      label: NotificationCategoryLabels[categoryOf(type)] ?? "Уведомление",
    }
  );
}

/* ───────────────────────── Date grouping ───────────────────────── */

type Bucket = { key: string; label: string; list: Notification[] };

function groupByDate(items: Notification[]): Bucket[] {
  const today = startOfDay(new Date());
  const yesterday = new Date(today.getTime() - 24 * 60 * 60 * 1000);
  const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);

  const buckets: Record<string, Bucket> = {
    today: { key: "today", label: "Сегодня", list: [] },
    yesterday: { key: "yesterday", label: "Вчера", list: [] },
    week: { key: "week", label: "На этой неделе", list: [] },
    earlier: { key: "earlier", label: "Раньше", list: [] },
  };

  for (const item of items) {
    const ts = new Date(item.createdAt).getTime();
    if (ts >= today.getTime()) buckets.today.list.push(item);
    else if (ts >= yesterday.getTime()) buckets.yesterday.list.push(item);
    else if (ts >= weekAgo.getTime()) buckets.week.list.push(item);
    else buckets.earlier.list.push(item);
  }

  // «Раньше» делим по конкретным датам, чтобы не было гигантского блока
  const earlier = buckets.earlier.list;
  const ordered: Bucket[] = [buckets.today, buckets.yesterday, buckets.week].filter(
    (b) => b.list.length > 0,
  );

  if (earlier.length > 0) {
    const byDay = new Map<string, Notification[]>();
    for (const item of earlier) {
      const d = startOfDay(new Date(item.createdAt));
      const key = d.toISOString();
      if (!byDay.has(key)) byDay.set(key, []);
      byDay.get(key)!.push(item);
    }
    const dayBuckets = [...byDay.entries()]
      .sort((a, b) => (a[0] < b[0] ? 1 : -1))
      .map<Bucket>(([key, list]) => ({
        key: `day-${key}`,
        label: formatShortDate(key),
        list,
      }));
    ordered.push(...dayBuckets);
  }

  return ordered;
}

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}
