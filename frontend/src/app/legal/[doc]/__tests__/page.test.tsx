import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import example from "../../../../../business-details.example.json";
import { loadBusinessDetails } from "@/shared/business-details/server";
import type * as Legal from "@/shared/legal";
import LegalDocPage from "../page";

vi.mock("server-only", () => ({}));
vi.mock("@/shared/business-details/server", () => ({ loadBusinessDetails: vi.fn() }));
vi.mock("@/shared/legal", async (importOriginal) => ({
  ...(await importOriginal<typeof Legal>()),
  loadLegalDocument: vi.fn().mockResolvedValue("# Legal test fixture"),
}));

async function renderDocument() {
  return render(await LegalDocPage({ params: Promise.resolve({ doc: "offer" }) }));
}

describe("private runtime contact for archived legal documents", () => {
  beforeEach(() => vi.clearAllMocks());

  it.each([example.email, "archive@example.test"])(
    "uses the operator-supplied archive contact %s",
    async (email) => {
      vi.mocked(loadBusinessDetails).mockResolvedValue({ ...example, email });
      await renderDocument();
      expect(screen.getByRole("link", { name: email })).toHaveAttribute("href", `mailto:${email}`);
      expect(screen.getByRole("heading", { name: "Legal test fixture" })).toBeInTheDocument();
    },
  );

  it("omits the archive contact when private business details are absent", async () => {
    vi.mocked(loadBusinessDetails).mockResolvedValue(null);
    const { container } = await renderDocument();
    expect(container.querySelector('a[href^="mailto:"]')).toBeNull();
    expect(screen.queryByText(/Архив всех версий доступен/)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Скачать PDF" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Legal test fixture" })).toBeInTheDocument();
  });
});
