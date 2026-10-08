import { NotificationTypes, type Notification } from "@/entities/notification";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificationBell } from "../ui/notification-bell";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

vi.mock("@/shared/auth", () => ({
  useIsAuthenticated: () => true,
}));

vi.mock("../model/use-notification-stream", () => ({
  useNotificationStream: () => undefined,
}));

vi.mock("../model/use-unread-count", () => ({
  useUnreadCount: () => ({ count: 0 }),
}));

vi.mock("../model/use-mark-all-as-read", () => ({
  useMarkAllAsRead: () => ({ markAllAsRead: vi.fn(), isPending: false }),
}));

vi.mock("../model/use-mark-as-read", () => ({
  useMarkAsRead: () => ({ markAsRead: vi.fn() }),
}));

const notifications: Notification[] = [];
vi.mock("../model/use-notifications", () => ({
  useNotifications: () => ({
    items: notifications,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
    isLoading: false,
    isFetchingNextPage: false,
    error: null,
    refetch: vi.fn(),
  }),
}));

function makeNotification(overrides?: Partial<Notification>): Notification {
  return {
    id: "n1",
    type: NotificationTypes.Welcome,
    templateId: "welcome",
    title: "Уведомление без ссылки",
    body: "Полный текст уведомления.",
    payload: "{}",
    targetUrl: "",
    channels: 1,
    createdAt: new Date().toISOString(),
    readAt: new Date().toISOString(),
    correlationId: null,
    ...overrides,
  };
}

beforeEach(() => {
  push.mockClear();
  notifications.length = 0;
});

describe("NotificationBell — диалог полного текста живёт вне Popover (#708)", () => {
  it("клик по уведомлению без targetUrl: поповер закрывается, диалог остаётся смонтированным", () => {
    notifications.push(makeNotification());
    render(<NotificationBell />);

    // Открыть поповер колокольчика.
    fireEvent.click(screen.getByRole("button", { name: /уведомления/i }));
    expect(screen.getByText("Уведомление без ссылки")).toBeInTheDocument();

    // Клик по уведомлению без ссылки.
    fireEvent.click(screen.getByText("Уведомление без ссылки"));

    // Поповер закрыт (список исчез), а диалог с полным текстом — смонтирован.
    expect(screen.queryByText("Прочитать все")).not.toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Полный текст уведомления.")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("клик по уведомлению со ссылкой: навигация, диалог не открывается", () => {
    notifications.push(makeNotification({ targetUrl: "/courses/dotnet" }));
    render(<NotificationBell />);

    fireEvent.click(screen.getByRole("button", { name: /уведомления/i }));
    fireEvent.click(screen.getByText("Уведомление без ссылки"));

    expect(push).toHaveBeenCalledWith("/courses/dotnet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
