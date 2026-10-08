export type TelegramChatType = "SUPERGROUP" | "CHANNEL";

export interface ChatBindingDto {
  id: string;
  planId: string;
  telegramChatId: number;
  chatType: TelegramChatType;
  chatTitle: string | null;
  inviteLink: string;
  enrollmentGrantsMembership: boolean;
  membershipGrantsEnrollment: boolean;
  autoKickOnRevoke: boolean;
  enforceMembership: boolean;
  createdAt: string;
  // message_id закреплённого claim-объявления. null = ещё не опубликовано (или бот не смог —
  // нет прав публиковать/закреплять). UI показывает статус под тумблером reverse-claim.
  announcementMessageId: number | null;
}

export interface MyChatBindingDto {
  // Дедуп идёт на бэке (один чат может быть привязан к нескольким планам, на которые у юзера grant).
  // planIds агрегирует все активные планы пользователя, шарящие этот чат.
  planIds: string[];
  // Index-aligned с planIds.
  planTitles: string[];
  telegramChatId: number;
  chatType: TelegramChatType;
  chatTitle: string | null;
  inviteLink: string;
  // true если юзер уже в чате (через getChatMember). UI показывает «Открыть» вместо «Войти».
  isMember: boolean;
}

export interface BindChatRequest {
  chatIdentifier: string;
  enrollmentGrantsMembership?: boolean;
  membershipGrantsEnrollment?: boolean;
  autoKickOnRevoke?: boolean;
  enforceMembership?: boolean;
}

export interface UpdateChatBindingFlagsRequest {
  enrollmentGrantsMembership: boolean;
  membershipGrantsEnrollment: boolean;
  autoKickOnRevoke: boolean;
  enforceMembership: boolean;
}
