import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { GetVideoChaptersResponse } from "@/entities/video";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { VideoChaptersEditor } from "../video-chapters-editor";

const http = vi.hoisted(() => ({
  get: vi.fn<(...args: unknown[]) => Promise<{ data: { result: GetVideoChaptersResponse } }>>(),
  put: vi.fn<(...args: unknown[]) => Promise<{ data: { result: GetVideoChaptersResponse } }>>(),
}));
vi.mock("@/shared/api/axios-instance", () => ({ apiClient: http, API_ORIGIN: "/api" }));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const stored: GetVideoChaptersResponse = {
  videoId: "video-one",
  chapters: [
    { id: "chapter-one", title: "Сохранённая глава", startSeconds: 12, sortOrder: 0 },
    { id: "chapter-two", title: "Удаляемая глава", startSeconds: 120, sortOrder: 1 },
  ],
};
const clients: QueryClient[] = [];

function mount(videoId = "video-one") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  clients.push(client);
  const view = render(
    <QueryClientProvider client={client}>
      <VideoChaptersEditor videoId={videoId} />
    </QueryClientProvider>,
  );
  return { ...view, client };
}

beforeEach(() => {
  vi.clearAllMocks();
  http.get.mockResolvedValue({ data: { result: stored } });
  http.put.mockResolvedValue({ data: { result: stored } });
});
afterEach(() => {
  cleanup();
  clients.splice(0).forEach((client) => client.clear());
});

describe("manual video chapters through the existing FileService API", () => {
  it("hydrates stored chapters and saves title/time edits, additions and deletions for this video", async () => {
    const { client } = mount();
    expect(await screen.findByLabelText("Название главы 1")).toHaveValue("Сохранённая глава");
    expect(screen.getByLabelText("Время начала главы 1")).toHaveValue("0:12");
    expect(http.get).toHaveBeenCalledWith("/videos/video-one/chapters/", {
      signal: expect.any(AbortSignal),
    });
    fireEvent.change(screen.getByLabelText("Название главы 1"), {
      target: { value: "  Уточнённая глава  " },
    });
    fireEvent.change(screen.getByLabelText("Время начала главы 1"), { target: { value: "0:30" } });
    fireEvent.click(screen.getByRole("button", { name: "Добавить главу" }));
    fireEvent.change(screen.getByLabelText("Название главы 1"), {
      target: { value: "Новая глава" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Удалить главу 3" }));
    const response: GetVideoChaptersResponse = {
      videoId: "video-one",
      chapters: [
        { id: "chapter-1", title: "Новая глава", startSeconds: 0, sortOrder: 0 },
        { id: "chapter-2", title: "Уточнённая глава", startSeconds: 30, sortOrder: 1 },
      ],
    };
    http.put.mockResolvedValueOnce({ data: { result: response } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    await waitFor(() =>
      expect(http.put).toHaveBeenCalledExactlyOnceWith("/videos/video-one/chapters/", {
        chapters: [
          { id: null, title: "Новая глава", startSeconds: 0, sortOrder: 1 },
          { id: "chapter-one", title: "Уточнённая глава", startSeconds: 30, sortOrder: 2 },
        ],
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Сохранить главы" })).toBeDisabled(),
    );
    expect(client.getQueryData(["videos", "chapters", "video-one"])).toEqual(response);
    expect(toast.success).toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /генер|обработ|AI/i })).not.toBeInTheDocument();
  });

  it("switches the chapter editor to the replacement video without writing the old video", async () => {
    const { client, rerender } = mount();
    await screen.findByLabelText("Название главы 1");
    fireEvent.change(screen.getByLabelText("Название главы 1"), {
      target: { value: "Старый черновик" },
    });
    http.get.mockResolvedValueOnce({
      data: {
        result: {
          videoId: "video-two",
          chapters: [{ id: "new-chapter", title: "Другое видео", startSeconds: 60, sortOrder: 0 }],
        },
      },
    });
    rerender(
      <QueryClientProvider client={client}>
        <VideoChaptersEditor videoId="video-two" />
      </QueryClientProvider>,
    );
    await waitFor(() =>
      expect(screen.getByLabelText("Название главы 1")).toHaveValue("Другое видео"),
    );
    fireEvent.change(screen.getByLabelText("Название главы 1"), {
      target: { value: "Новый черновик" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    await waitFor(() =>
      expect(http.put).toHaveBeenCalledExactlyOnceWith("/videos/video-two/chapters/", {
        chapters: [{ id: "new-chapter", title: "Новый черновик", startSeconds: 60, sortOrder: 1 }],
      }),
    );
  });

  it("waits for chapter hydration instead of offering an empty editable draft", async () => {
    let resolve!: (value: { data: { result: GetVideoChaptersResponse } }) => void;
    http.get.mockReturnValueOnce(
      new Promise((done) => {
        resolve = done;
      }),
    );
    mount();
    expect(screen.getByRole("status")).toHaveTextContent("Загружаем главы");
    expect(screen.queryByRole("button", { name: "Сохранить главы" })).not.toBeInTheDocument();
    await act(async () => {
      resolve({ data: { result: stored } });
    });
    expect(await screen.findByLabelText("Название главы 1")).toHaveValue("Сохранённая глава");
  });

  it.each(["Нет доступа к данному видео", "Провайдер временно недоступен"])(
    "does not offer edits when the read fails: %s",
    async (message) => {
      http.get.mockRejectedValueOnce(new Error(message));
      mount();
      expect(await screen.findByRole("alert")).toHaveTextContent(message);
      expect(screen.queryByLabelText("Название главы 1")).not.toBeInTheDocument();
      expect(http.put).not.toHaveBeenCalled();
      fireEvent.click(screen.getByRole("button", { name: "Повторить загрузку глав" }));
      expect(await screen.findByLabelText("Название главы 1")).toHaveValue("Сохранённая глава");
    },
  );

  it("disables editing and duplicate saves while the existing PUT is pending", async () => {
    let resolve!: (value: { data: { result: GetVideoChaptersResponse } }) => void;
    http.put.mockReturnValueOnce(
      new Promise((done) => {
        resolve = done;
      }),
    );
    mount();
    fireEvent.change(await screen.findByLabelText("Название главы 1"), {
      target: { value: "Черновик" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    await waitFor(() => expect(screen.getByLabelText("Название главы 1")).toBeDisabled());
    expect(screen.getByRole("button", { name: "Добавить главу" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Удалить главу 1" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    expect(http.put).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolve({ data: { result: stored } });
    });
    await waitFor(() => expect(screen.getByLabelText("Название главы 1")).toBeEnabled());
  });

  it.each(["Нет доступа к данному видео", "Не удалось сохранить главы"])(
    "retains edits after a rejected save, with a retry: %s",
    async (message) => {
      http.put.mockRejectedValueOnce(new Error(message));
      mount();
      fireEvent.change(await screen.findByLabelText("Название главы 1"), {
        target: { value: "Мой черновик" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
      await waitFor(() => expect(toast.error).toHaveBeenCalledWith(message));
      expect(screen.getByLabelText("Название главы 1")).toHaveValue("Мой черновик");
      expect(screen.getByRole("button", { name: "Сохранить главы" })).toBeEnabled();
      fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
      await waitFor(() => expect(http.put).toHaveBeenCalledTimes(2));
      await waitFor(() =>
        expect(screen.getByRole("button", { name: "Сохранить главы" })).toBeDisabled(),
      );
    },
  );

  it("blocks invalid and duplicate times without issuing a write", async () => {
    mount();
    fireEvent.change(await screen.findByLabelText("Время начала главы 1"), {
      target: { value: "0:99" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    expect(http.put).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("Время начала главы 1"), { target: { value: "2:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    expect(http.put).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledTimes(2);
  });

  it("allows deleting all chapters and adding a chapter to an empty video", async () => {
    mount();
    await screen.findByLabelText("Название главы 1");
    fireEvent.click(screen.getByRole("button", { name: "Удалить главу 2" }));
    fireEvent.click(screen.getByRole("button", { name: "Удалить главу 1" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить главы" }));
    await waitFor(() =>
      expect(http.put).toHaveBeenCalledWith("/videos/video-one/chapters/", { chapters: [] }),
    );
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Сохранить главы" })).toBeDisabled(),
    );
    fireEvent.click(screen.getAllByRole("button", { name: "Добавить главу" })[0]!);
    expect(screen.getByLabelText("Название главы 1")).toHaveValue("");
  });
});
