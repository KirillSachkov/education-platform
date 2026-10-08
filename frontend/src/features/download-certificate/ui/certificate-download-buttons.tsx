"use client";

import { useState } from "react";
import { toast } from "sonner";
import type { CourseCertificateDto } from "@/entities/certificate";
import { routes } from "@/shared/config/routes";
import { formatFullDate } from "@/shared/lib/date";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { CERTIFICATE_CANVAS_SIZE, drawCertificate } from "../lib/draw-certificate";
import { buildCertificateFileName } from "../lib/file-name";

type DownloadFormat = "pdf" | "png";

interface CertificateDownloadButtonsProps {
  certificate: CourseCertificateDto;
}

/**
 * Кнопки «Скачать PDF» / «Скачать картинку» на публичной странице сертификата (#650).
 * Рисует сертификат на off-screen канвасе (см. `drawCertificate`) и отдаёт PNG напрямую
 * либо вкладывает его одной страницей в PDF (jspdf, lazy-import — не грузит бандл, пока не
 * нажали). Без интеграции с LinkedIn — только файл.
 */
export function CertificateDownloadButtons({ certificate }: CertificateDownloadButtonsProps) {
  const [busy, setBusy] = useState<DownloadFormat | null>(null);

  async function handleDownload(format: DownloadFormat) {
    if (busy) {
      return;
    }
    setBusy(format);
    try {
      const canvas = document.createElement("canvas");
      canvas.width = CERTIFICATE_CANVAS_SIZE.width;
      canvas.height = CERTIFICATE_CANVAS_SIZE.height;
      const ctx = canvas.getContext("2d");
      if (!ctx) {
        toast.error("Браузер не поддерживает формирование сертификата");
        return;
      }

      drawCertificate(ctx, {
        holderName: certificate.holderName,
        courseTitle: certificate.courseTitle,
        issuedAtLabel: formatFullDate(certificate.issuedAt),
        serialNumber: certificate.serialNumber,
        verifyUrl: `${window.location.origin}${routes.certificates(certificate.id)}`,
      });

      const fileName = buildCertificateFileName(certificate.serialNumber, format);

      if (format === "png") {
        const blob = await canvasToBlob(canvas);
        if (!blob) {
          toast.error("Не удалось сформировать картинку");
          return;
        }
        downloadBlob(blob, fileName);
      } else {
        const { jsPDF } = await import("jspdf");
        const pdf = new jsPDF({
          orientation: "landscape",
          unit: "px",
          format: [CERTIFICATE_CANVAS_SIZE.width, CERTIFICATE_CANVAS_SIZE.height],
        });
        pdf.addImage(
          canvas.toDataURL("image/png"),
          "PNG",
          0,
          0,
          CERTIFICATE_CANVAS_SIZE.width,
          CERTIFICATE_CANVAS_SIZE.height,
        );
        pdf.save(fileName);
      }
    } catch {
      toast.error("Не удалось сформировать сертификат");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="flex flex-wrap items-center justify-center gap-2">
      <Button
        type="button"
        variant="outline"
        onClick={() => handleDownload("pdf")}
        disabled={busy !== null}
        className="gap-1.5"
      >
        {busy === "pdf" ? (
          <Icons.loading className="size-4 animate-spin" />
        ) : (
          <Icons.document className="size-4" />
        )}
        Скачать PDF
      </Button>
      <Button
        type="button"
        variant="outline"
        onClick={() => handleDownload("png")}
        disabled={busy !== null}
        className="gap-1.5"
      >
        {busy === "png" ? (
          <Icons.loading className="size-4 animate-spin" />
        ) : (
          <Icons.fileImage className="size-4" />
        )}
        Скачать картинку
      </Button>
    </div>
  );
}

function canvasToBlob(canvas: HTMLCanvasElement): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob), "image/png"));
}

function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // Отложенный revoke: на мобильном Safari (iOS 15/16) скачивание стартует асинхронно,
  // синхронный revokeObjectURL прерывает его. Освобождаем blob позже.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
