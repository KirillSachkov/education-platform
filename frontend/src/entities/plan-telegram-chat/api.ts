import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  BindChatRequest,
  ChatBindingDto,
  MyChatBindingDto,
  UpdateChatBindingFlagsRequest,
} from "./types";

export const planTelegramChatApi = {
  list: async (planId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<ChatBindingDto[]>>(
      `/telegram/admin/plans/${planId}/chat-bindings/`,
      { signal },
    );
    return res.data;
  },

  bind: async (planId: string, request: BindChatRequest) => {
    const res = await apiClient.post<Envelope<ChatBindingDto>>(
      `/telegram/admin/plans/${planId}/chat-bindings/`,
      request,
    );
    return res.data;
  },

  unbind: async (bindingId: string) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/telegram/admin/chat-bindings/${bindingId}/`,
    );
    return res.data;
  },

  updateFlags: async (bindingId: string, request: UpdateChatBindingFlagsRequest) => {
    const res = await apiClient.patch<Envelope<ChatBindingDto>>(
      `/telegram/admin/chat-bindings/${bindingId}/`,
      request,
    );
    return res.data;
  },

  myChats: async (signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<MyChatBindingDto[]>>(`/telegram/me/chats/`, {
      signal,
    });
    return res.data;
  },
};

export const planTelegramChatQueryOptions = {
  baseKey: "telegram",

  list: (planId: string) =>
    queryOptions({
      queryKey: [
        planTelegramChatQueryOptions.baseKey,
        "admin",
        "plans",
        planId,
        "chat-bindings",
      ],
      queryFn: ({ signal }) => planTelegramChatApi.list(planId, signal),
      select: (data) => data.result ?? [],
    }),

  myChats: () =>
    queryOptions({
      queryKey: [planTelegramChatQueryOptions.baseKey, "me", "chats"] as const,
      queryFn: ({ signal }) => planTelegramChatApi.myChats(signal),
      select: (data) => data.result ?? [],
    }),
};
