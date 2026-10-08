import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EmailStep } from "../email-step";

describe("EmailStep auth copy", () => {
  it("says the email code signs in or creates an account", () => {
    render(<EmailStep onSubmit={vi.fn()} isPending={false} />);

    expect(screen.getByText("Вход или регистрация")).toBeInTheDocument();
    expect(
      screen.getByText("По коду из письма вы войдёте в аккаунт или создадите новый."),
    ).toBeInTheDocument();
  });

  it("shows generic purchase context without echoing the plan slug", () => {
    render(<EmailStep onSubmit={vi.fn()} isPending={false} hasPricingIntent />);

    expect(
      screen.getByText("После входа вернём вас к оформлению выбранного плана."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/dotnet-fullstack/i)).not.toBeInTheDocument();
  });
});
