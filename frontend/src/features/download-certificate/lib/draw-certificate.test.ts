import { describe, expect, it, vi } from "vitest";
import { drawCertificate } from "./draw-certificate";

function createMockCtx() {
  const fillTexts: string[] = [];
  const ctx = {
    fillStyle: "",
    strokeStyle: "",
    lineWidth: 0,
    font: "",
    textAlign: "left" as CanvasTextAlign,
    textBaseline: "alphabetic" as CanvasTextBaseline,
    fillRect: vi.fn(),
    strokeRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    stroke: vi.fn(),
    fillText: vi.fn((text: string) => fillTexts.push(text)),
    // Грубая, но монотонная оценка ширины — достаточно для логики переноса/ужатия.
    measureText: vi.fn((text: string) => ({ width: text.length * 18 })),
  };
  return { ctx: ctx as unknown as CanvasRenderingContext2D, fillTexts };
}

const data = {
  holderName: "Иван Иванов",
  courseTitle: "Архитектура .NET",
  issuedAtLabel: "25 июня 2026",
  serialNumber: "CERT-ABC123DEF456",
  verifyUrl: "https://sachkov-learn.net/certificates/abc",
};

describe("drawCertificate", () => {
  it("рисует имя, курс, дату, серийник, дисклеймер и ссылку проверки", () => {
    const { ctx, fillTexts } = createMockCtx();

    drawCertificate(ctx, data);

    const all = fillTexts.join("\n");
    expect(fillTexts).toContain("Иван Иванов");
    expect(all).toContain("Архитектура .NET");
    expect(all).toContain("25 июня 2026");
    expect(fillTexts).toContain("CERT-ABC123DEF456");
    expect(all).toContain("не является документом об образовании");
    expect(all).toContain(data.verifyUrl);
  });

  it("переносит длинный заголовок курса, сохраняя все слова", () => {
    const { ctx, fillTexts } = createMockCtx();
    const longTitle = "Полный курс по проектированию распределённых систем на платформе .NET";

    drawCertificate(ctx, { ...data, courseTitle: longTitle });

    const all = fillTexts.join(" ");
    for (const word of longTitle.split(" ")) {
      expect(all).toContain(word);
    }
  });
});
