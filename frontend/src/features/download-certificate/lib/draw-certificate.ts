/**
 * Отрисовка сертификата на 2D-канвасе для скачивания PDF/PNG (#650). Намеренно
 * self-contained: светлый печатный документ с hex-цветами и системными шрифтами —
 * не зависит от темы сайта, oklch-токенов и веб-шрифтов, поэтому результат
 * детерминирован и одинаков в любом браузере. Чистая функция (только рисует в
 * переданный контекст) — покрыта unit-тестом с mock-контекстом.
 */

export interface CertificateDrawData {
  holderName: string;
  courseTitle: string;
  /** Уже отформатированная дата выдачи, напр. «25 июня 2026». */
  issuedAtLabel: string;
  serialNumber: string;
  /** Абсолютная ссылка на публичную страницу проверки. */
  verifyUrl: string;
}

export interface CertificateCanvasSize {
  width: number;
  height: number;
}

/** Холст сертификата — пропорции A4 landscape (√2), высокое разрешение для печати. */
export const CERTIFICATE_CANVAS_SIZE: CertificateCanvasSize = { width: 2000, height: 1414 };

const BRAND = "#6366f1";
const INK = "#0f172a";
const MUTED = "#475569";
const FAINT = "#94a3b8";
const SERIF = "Georgia, 'Times New Roman', serif";
const SANS = "Arial, 'Helvetica Neue', sans-serif";

const DISCLAIMER =
  "Документ имеет исключительно информационный характер, подтверждает факт прохождения " +
  "курса и не является документом об образовании или о квалификации.";

function wrapLines(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  const lines: string[] = [];
  let current = "";
  for (const word of words) {
    const candidate = current ? `${current} ${word}` : word;
    if (current && ctx.measureText(candidate).width > maxWidth) {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current) {
    lines.push(current);
  }
  return lines;
}

/** Рисует одну строку по центру, ужимая кегль пока не влезет в maxWidth. */
function drawFittedLine(
  ctx: CanvasRenderingContext2D,
  text: string,
  centerX: number,
  baselineY: number,
  maxWidth: number,
  weight: string,
  basePx: number,
  family: string,
): void {
  let px = basePx;
  ctx.font = `${weight} ${px}px ${family}`;
  while (px > 24 && ctx.measureText(text).width > maxWidth) {
    px -= 4;
    ctx.font = `${weight} ${px}px ${family}`;
  }
  ctx.fillText(text, centerX, baselineY);
}

export function drawCertificate(
  ctx: CanvasRenderingContext2D,
  data: CertificateDrawData,
  size: CertificateCanvasSize = CERTIFICATE_CANVAS_SIZE,
): void {
  const { width: w, height: h } = size;
  const cx = w / 2;
  const inner = w - 380;

  // Фон + двойная брендовая рамка.
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, w, h);
  ctx.strokeStyle = BRAND;
  ctx.lineWidth = 10;
  ctx.strokeRect(60, 60, w - 120, h - 120);
  ctx.lineWidth = 2;
  ctx.strokeRect(86, 86, w - 172, h - 172);

  ctx.textAlign = "center";
  ctx.textBaseline = "alphabetic";

  // Шапка.
  ctx.fillStyle = BRAND;
  ctx.font = `600 36px ${SERIF}`;
  ctx.fillText("SACHKOVLEARN", cx, 240);

  ctx.fillStyle = MUTED;
  ctx.font = `28px ${SANS}`;
  ctx.fillText("СЕРТИФИКАТ О ПРОХОЖДЕНИИ", cx, 312);

  // Имя получателя (крупно, с автоужатием).
  ctx.fillStyle = INK;
  drawFittedLine(ctx, data.holderName, cx, 520, inner, "700", 92, SERIF);

  // Курс.
  ctx.fillStyle = MUTED;
  ctx.font = `30px ${SANS}`;
  ctx.fillText("успешно прошёл(а) курс", cx, 600);

  ctx.fillStyle = INK;
  ctx.font = `600 56px ${SERIF}`;
  const allTitleLines = wrapLines(ctx, `«${data.courseTitle}»`, inner);
  const titleLines = allTitleLines.slice(0, 3);
  if (allTitleLines.length > titleLines.length && titleLines.length > 0) {
    // Заголовок не влез в 3 строки — гарантируем закрывающую кавычку (юр. документ).
    const last = titleLines.length - 1;
    titleLines[last] = `${titleLines[last].replace(/[»…]+$/, "")}…»`;
  }
  titleLines.forEach((line, i) => {
    ctx.fillText(line, cx, 685 + i * 72);
  });

  // Разделитель.
  const dividerY = 685 + titleLines.length * 72 + 40;
  ctx.strokeStyle = "#e2e8f0";
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(cx - 120, dividerY);
  ctx.lineTo(cx + 120, dividerY);
  ctx.stroke();

  // Дата, серийник, отметка проверки.
  ctx.fillStyle = MUTED;
  ctx.font = `26px ${SANS}`;
  ctx.fillText(`Выдан ${data.issuedAtLabel}`, cx, dividerY + 70);

  ctx.fillStyle = INK;
  ctx.font = `600 26px 'Courier New', monospace`;
  ctx.fillText(data.serialNumber, cx, dividerY + 118);

  ctx.fillStyle = BRAND;
  ctx.font = `24px ${SANS}`;
  ctx.fillText("✓ Проверено платформой", cx, dividerY + 166);

  // Дисклеймер (152-ФЗ / ЗоЗПП — п.3.3 оферты) + ссылка проверки внизу.
  ctx.fillStyle = FAINT;
  ctx.font = `22px ${SANS}`;
  const disclaimerLines = wrapLines(ctx, DISCLAIMER, w - 320);
  const disclaimerTop = h - 200;
  disclaimerLines.forEach((line, i) => {
    ctx.fillText(line, cx, disclaimerTop + i * 32);
  });
  ctx.fillText(`Проверка: ${data.verifyUrl}`, cx, h - 110);
}
