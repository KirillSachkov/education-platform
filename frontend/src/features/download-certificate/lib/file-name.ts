/**
 * Имя скачиваемого файла сертификата: `Сертификат-CERT-XXXX.{ext}`.
 * Серийник чистится до `[A-Za-z0-9-]`, чтобы имя файла было безопасным; пустой/битый
 * серийник даёт fallback «Сертификат».
 */
export function buildCertificateFileName(serialNumber: string, ext: "pdf" | "png"): string {
  const safeSerial = serialNumber.replace(/[^A-Za-z0-9-]/g, "");
  return safeSerial.length > 0 ? `Сертификат-${safeSerial}.${ext}` : `Сертификат.${ext}`;
}
