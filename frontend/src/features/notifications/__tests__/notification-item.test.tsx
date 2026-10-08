import { NotificationTypes, type Notification } from "@/entities/notification";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificationItem } from "../ui/notification-item";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

const markAsRead = vi.fn();
vi.mock("../model/use-mark-as-read", () => ({
  useMarkAsRead: () => ({ markAsRead }),
}));

function makeNotification(overrides?: Partial<Notification>): Notification {
  return {
    id: "n1",
    type: NotificationTypes.Welcome,
    templateId: "welcome",
    title: "Заголовок",
    body: "Тело уведомления",
    payload: "{}",
    targetUrl: "",
    channels: 1,
    createdAt: new Date().toISOString(),
    readAt: null,
    correlationId: null,
    ...overrides,
  };
}

beforeEach(() => {
  push.mockClear();
  markAsRead.mockClear();
});

describe("NotificationItem", () => {
  it("с targetUrl: клик ведёт по ссылке и НЕ открывает модалку", () => {
    const onNavigate = vi.fn();
    const onOpenDetail = vi.fn();
    render(
      <NotificationItem
        notification={makeNotification({ targetUrl: "/courses/dotnet" })}
        onNavigate={onNavigate}
        onOpenDetail={onOpenDetail}
      />,
    );

    fireEvent.click(screen.getByRole("button"));

    expect(markAsRead).toHaveBeenCalledWith("n1");
    expect(onNavigate).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith("/courses/dotnet");
    expect(onOpenDetail).not.toHaveBeenCalled();
  });

  it("без targetUrl: клик открывает модалку и НЕ навигирует (#708 — не на главную)", () => {
    const onOpenDetail = vi.fn();
    const notification = makeNotification({ targetUrl: "" });
    render(<NotificationItem notification={notification} onOpenDetail={onOpenDetail} />);

    fireEvent.click(screen.getByRole("button"));

    expect(onOpenDetail).toHaveBeenCalledWith(notification);
    expect(push).not.toHaveBeenCalled();
  });

  it("без targetUrl прочитанное с onOpenDetail — кликабельно", () => {
    render(
      <NotificationItem
        notification={makeNotification({ readAt: new Date().toISOString() })}
        onOpenDetail={vi.fn()}
      />,
    );

    expect(screen.getByRole("button")).toBeEnabled();
  });

  it("прочитанное, без ссылки и без onOpenDetail — disabled (клику некуда вести)", () => {
    render(
      <NotificationItem notification={makeNotification({ readAt: new Date().toISOString() })} />,
    );

    expect(screen.getByRole("button")).toBeDisabled();
  });

  it("непрочитанное без ссылки: markAsRead вызывается вместе с открытием модалки", () => {
    const onOpenDetail = vi.fn();
    render(<NotificationItem notification={makeNotification()} onOpenDetail={onOpenDetail} />);

    fireEvent.click(screen.getByRole("button"));

    expect(markAsRead).toHaveBeenCalledWith("n1");
    expect(onOpenDetail).toHaveBeenCalled();
  });
});
