import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { VideoChaptersList } from "../video-chapters-list";

describe("retained video chapters", () => {
  it("seeks to a stored chapter without a processing service", () => {
    const onSeek = vi.fn();
    render(
      <VideoChaptersList
        chapters={[
          { title: "Введение", timeSeconds: 0 },
          { title: "Практика", timeSeconds: 120 },
        ]}
        activeIndex={0}
        onSeek={onSeek}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /Практика/ }));

    expect(onSeek).toHaveBeenCalledWith(120);
  });
});
