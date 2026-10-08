"use client";

import { Icons } from "@/shared/ui/icons";

const MESSAGES: Record<
  string,
  { tone: "success" | "info" | "error"; title: string; description: string }
> = {
  "sync-success": {
    tone: "success",
    title: "Курсы синхронизированы",
    description: "Курсы по вашим GitHub-организациям были привязаны.",
  },
  "sync-no_orgs": {
    tone: "info",
    title: "Организации не найдены",
    description: "Не найдено GitHub-организаций, к которым привязаны курсы.",
  },
  "sync-error": {
    tone: "error",
    title: "Ошибка синхронизации",
    description: "Не удалось синхронизировать курсы через GitHub.",
  },
  "sync-email_mismatch": {
    tone: "error",
    title: "Email на GitHub не совпадает",
    description:
      "Email вашего GitHub-аккаунта отличается от email на платформе. Сначала привяжите GitHub в профиле.",
  },
  "account-github-linked": {
    tone: "success",
    title: "GitHub привязан",
    description: "Аккаунт GitHub успешно привязан к вашему профилю.",
  },
  "account-github-already-linked": {
    tone: "info",
    title: "GitHub уже привязан",
    description: "К вашему аккаунту уже привязан GitHub. Сначала отвяжите его.",
  },
  "account-github-link-conflict": {
    tone: "error",
    title: "Этот GitHub занят",
    description:
      "Этот GitHub-аккаунт уже привязан к другому пользователю платформы. " +
      "Войдите под тем аккаунтом и отвяжите GitHub либо используйте другой GitHub-аккаунт.",
  },
  "account-github-link-failed": {
    tone: "error",
    title: "Не удалось привязать GitHub",
    description: "Попробуйте ещё раз. Если ошибка повторится — напишите в поддержку.",
  },
};

const TONE_CLASSES = {
  success: "border-green/20 bg-green-dim text-green dark:border-green/30",
  info: "border-blue/20 bg-blue-dim text-blue dark:border-blue/30",
  error: "border-red/20 bg-red-dim text-red dark:border-red/30",
};

export function AccountStatusBanner({
  status,
  onDismiss,
}: {
  status: string | null;
  onDismiss: () => void;
}) {
  if (!status) return null;
  const message = MESSAGES[status];
  if (!message) return null;

  return (
    <div className={`relative rounded-xl border px-4 py-3 ${TONE_CLASSES[message.tone]}`}>
      <button
        onClick={onDismiss}
        className="absolute top-2.5 right-2.5 opacity-60 hover:opacity-100 transition-opacity"
        aria-label="Закрыть"
      >
        <Icons.close size={14} />
      </button>
      <p className="text-sm font-semibold pr-6">{message.title}</p>
      <p className="mt-1 text-sm/6 opacity-90 pr-6">{message.description}</p>
    </div>
  );
}
