import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// Страница — async server component: auth() и redirect() мокаются, клиентские
// дети (LoginForm, LoginProviders) заменяются лёгкими заглушками — здесь
// тестируется состав страницы (баннер ошибок, отсутствие GitHub-кнопки).
vi.mock("@/shared/auth/auth", () => ({ auth: vi.fn().mockResolvedValue(null) }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("@/features/auth-login", () => ({
  LoginForm: () => <div data-testid="login-form" />,
}));
vi.mock("../providers", () => ({
  LoginProviders: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

import LoginPage from "../page";

async function renderLoginPage(params: { error?: string; callbackUrl?: string } = {}) {
  const jsx = await LoginPage({ searchParams: Promise.resolve(params) });
  return render(jsx);
}

describe("LoginPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the OTP wizard only — no GitHub button and no «или» divider", async () => {
    await renderLoginPage();

    expect(screen.getByTestId("login-form")).toBeInTheDocument();
    expect(screen.queryByText("Войти через GitHub")).not.toBeInTheDocument();
    expect(screen.queryByText("или")).not.toBeInTheDocument();
  });

  it("shows the legal banner for ?error=github-login-disabled", async () => {
    await renderLoginPage({ error: "github-login-disabled" });

    expect(
      screen.getByText(
        "Вход через GitHub отключён — так требует закон. Введите почту вашего GitHub-аккаунта: аккаунт тот же, код придёт на неё.",
      ),
    ).toBeInTheDocument();
  });

  it("keeps the legacy github_auth_failed message (link-flow still emits it)", async () => {
    await renderLoginPage({ error: "github_auth_failed" });

    expect(screen.getByText("Не удалось подключить GitHub. Попробуйте ещё раз из настроек")).toBeInTheDocument();
  });

  it("keeps the legacy github_email_required message (link-flow still emits it)", async () => {
    await renderLoginPage({ error: "github_email_required" });

    expect(
      screen.getByText("У GitHub аккаунта должен быть публичный email"),
    ).toBeInTheDocument();
  });

  it("shows no banner for unknown error codes", async () => {
    const { container } = await renderLoginPage({ error: "some_unknown_code" });

    expect(container.querySelector(".text-red")).not.toBeInTheDocument();
  });
});
