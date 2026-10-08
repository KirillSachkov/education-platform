"use client";

import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import { useCourseSubscription } from "../model/use-course-subscription";

interface CourseSubscribeButtonProps {
  courseId: string;
  /** Стиль: "icon" — компактный круглый toggle; "labeled" — кнопка с текстом. */
  variant?: "icon" | "labeled";
  className?: string;
}

/**
 * Toggle подписки на курс. Подписчики получают уведомления о новых материалах
 * и заданиях. При зачислении подписка создаётся автоматически — этот toggle
 * нужен для отписки или для незаписанных пользователей, которые хотят следить
 * за курсом.
 */
export function CourseSubscribeButton({
  courseId,
  variant = "icon",
  className,
}: CourseSubscribeButtonProps) {
  const { isSubscribed, isLoading, isPending, subscribe, unsubscribe } =
    useCourseSubscription(courseId);

  const handleClick = () => {
    if (isPending) return;
    void (isSubscribed ? unsubscribe() : subscribe());
  };

  const label = isSubscribed ? "Уведомления включены" : "Включить уведомления";
  const Icon = isSubscribed ? Icons.notification : Icons.notificationOff;

  if (variant === "labeled") {
    return (
      <Button
        type="button"
        variant={isSubscribed ? "secondary" : "outline"}
        size="sm"
        onClick={handleClick}
        disabled={isLoading || isPending}
        className={className}
        aria-pressed={isSubscribed}
        title={label}
      >
        <Icon size={14} className="mr-1.5" />
        {label}
      </Button>
    );
  }

  return (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      onClick={handleClick}
      disabled={isLoading || isPending}
      className={cn(
        "size-9 rounded-full",
        isSubscribed && "text-primary",
        className,
      )}
      aria-pressed={isSubscribed}
      aria-label={label}
      title={label}
    >
      <Icon size={18} />
    </Button>
  );
}
