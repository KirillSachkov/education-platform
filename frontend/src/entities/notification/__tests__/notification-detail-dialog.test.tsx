import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { NotificationTypes, type Notification } from "../model/types";
import { NotificationDetailDialog } from "../ui/notification-detail-dialog";

function makeNotification(overrides?: Partial<Notification>): Notification {
  return {
    id: "n1",
    type: NotificationTypes.Welcome,
    templateId: "welcome",
    title: "Очень длинный заголовок уведомления без ссылки",
    body: "Первая строка.\nВторая строка длинного текста уведомления.",
    payload: "{}",
    targetUrl: "",
    channels: 1,
    createdAt: new Date().toISOString(),
    readAt: null,
    correlationId: null,
    ...overrides,
  };
}

describe("NotificationDetailDialog", () => {
  it("показывает полный заголовок, тело и метку типа", () => {
    render(<NotificationDetailDialog notification={makeNotification()} onOpenChange={vi.fn()} />);

    expect(
      screen.getByText("Очень длинный заголовок уведомления без ссылки"),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Первая строка\.\s*Вторая строка длинного текста уведомления\./),
    ).toBeInTheDocument();
    expect(screen.getByText(/Приветствие/)).toBeInTheDocument();
  });

  it("уведомление без body: рендерит только заголовок, без пустого блока", () => {
    render(
      <NotificationDetailDialog
        notification={makeNotification({ body: "" })}
        onOpenChange={vi.fn()}
      />,
    );

    expect(
      screen.getByText("Очень длинный заголовок уведомления без ссылки"),
    ).toBeInTheDocument();
  });

  it("null → диалог закрыт, ничего не рендерится", () => {
    render(<NotificationDetailDialog notification={null} onOpenChange={vi.fn()} />);

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("закрытие крестиком дёргает onOpenChange(false)", () => {
    const onOpenChange = vi.fn();
    render(
      <NotificationDetailDialog notification={makeNotification()} onOpenChange={onOpenChange} />,
    );

    fireEvent.click(screen.getByRole("button", { name: /close/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
