import { NotificationTypes, type Notification } from "@/entities/notification";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificationsCenter } from "../notifications-center";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

const markAsRead = vi.fn();
const notifications: Notification[] = [];
vi.mock("@/features/notifications", () => ({
  useNotifications: () => ({
    items: notifications,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
    isLoading: false,
    isFetchingNextPage: false,
    error: null,
    refetch: vi.fn(),
  }),
  useUnreadCount: () => ({ count: 0 }),
  useMarkAsRead: () => ({ markAsRead }),
  useMarkAllAsRead: () => ({ markAllAsRead: vi.fn(), isPending: false }),
}));

function makeNotification(overrides?: Partial<Notification>): Notification {
  return {
    id: "n1",
    type: NotificationTypes.Welcome,
    templateId: "welcome",
    title: "Уведомление без ссылки",
    body: "Полный текст, который в списке обрезан line-clamp'ом.",
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
  markAsRead.mockClear();
  notifications.length = 0;
});

describe("NotificationsCenter (страница /notifications)", () => {
  it("клик по уведомлению без targetUrl открывает модалку с полным текстом, без навигации (#708)", () => {
    notifications.push(makeNotification());
    render(<NotificationsCenter />);

    fireEvent.click(screen.getByText("Уведомление без ссылки"));

    expect(push).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    // Полный текст виден в модалке (второй экземпляр текста — внутри диалога).
    expect(
      screen.getAllByText("Полный текст, который в списке обрезан line-clamp'ом."),
    ).toHaveLength(2);
  });

  it("клик по уведомлению с targetUrl навигирует, модалка не открывается", () => {
    notifications.push(makeNotification({ targetUrl: "/courses/dotnet" }));
    render(<NotificationsCenter />);

    fireEvent.click(screen.getByText("Уведомление без ссылки"));

    expect(push).toHaveBeenCalledWith("/courses/dotnet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("Escape закрывает открытую модалку", () => {
    notifications.push(makeNotification());
    render(<NotificationsCenter />);

    fireEvent.click(screen.getByText("Уведомление без ссылки"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
