import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ComponentProps } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TagDto } from "@/entities/tag";
import { SearchTagPicker } from "../search-tag-picker";

const { popularOptions, suggestOptions } = vi.hoisted(() => ({
  popularOptions: vi.fn(),
  suggestOptions: vi.fn(),
}));

vi.mock("@/entities/tag", () => ({
  tagsQueryOptions: {
    popular: popularOptions,
    suggest: suggestOptions,
  },
}));

const scopedTags: TagDto[] = [
  { id: "k8s", title: "Kubernetes", slug: "kubernetes", kind: "canon" },
  { id: "pod", title: "Pod", slug: "pod", kind: "canon" },
];

function renderPicker(props: Partial<ComponentProps<typeof SearchTagPicker>> = {}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={client}>
      <SearchTagPicker selectedTags={[]} onAdd={vi.fn()} onRemove={vi.fn()} {...props} />
    </QueryClientProvider>,
  );
}

describe("SearchTagPicker", () => {
  beforeEach(() => {
    globalThis.ResizeObserver = class ResizeObserver {
      observe() {}
      unobserve() {}
      disconnect() {}
    };
    Element.prototype.scrollIntoView = vi.fn();

    popularOptions.mockReset();
    suggestOptions.mockReset();

    popularOptions.mockReturnValue({
      queryKey: ["tags", "popular"],
      queryFn: async () => [{ id: "react", title: "React", slug: "react", kind: "canon" }],
    });

    suggestOptions.mockReturnValue({
      queryKey: ["tags", "suggest"],
      queryFn: async () => [],
      enabled: false,
    });
  });

  it("does not request popular tags before the user types", () => {
    renderPicker();

    expect(popularOptions).not.toHaveBeenCalled();
  });

  it("filters scoped suggestions after user input", async () => {
    const onAdd = vi.fn();
    const user = userEvent.setup();

    renderPicker({ suggestionTags: scopedTags, onAdd });

    await user.click(screen.getByRole("button", { name: /тег/i }));
    await user.type(screen.getByPlaceholderText("Поиск тегов..."), "kub");
    await user.click(await screen.findByText("Kubernetes"));

    expect(onAdd).toHaveBeenCalledWith(scopedTags[0]);
  });
});
