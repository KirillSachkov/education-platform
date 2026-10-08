import { describe, expect, it } from "vitest";
import { buildCertificateFileName } from "./file-name";

describe("buildCertificateFileName", () => {
  it("строит имя из серийника и расширения", () => {
    expect(buildCertificateFileName("CERT-ABC123DEF456", "pdf")).toBe("Сертификат-CERT-ABC123DEF456.pdf");
    expect(buildCertificateFileName("CERT-ABC123DEF456", "png")).toBe("Сертификат-CERT-ABC123DEF456.png");
  });

  it("вычищает небезопасные символы из серийника", () => {
    expect(buildCertificateFileName("CERT/../x y", "pdf")).toBe("Сертификат-CERTxy.pdf");
  });

  it("даёт fallback для пустого/битого серийника", () => {
    expect(buildCertificateFileName("", "png")).toBe("Сертификат.png");
    expect(buildCertificateFileName("////", "pdf")).toBe("Сертификат.pdf");
  });
});
