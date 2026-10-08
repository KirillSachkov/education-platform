import { createRef } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  KinescopeInteractivePlayer,
  type KinescopeInteractivePlayerHandle,
} from "../kinescope-interactive-player";

const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

const player = {
  Events: {
    TimeUpdate: "timeupdate",
    SeekChapter: "seekchapter",
  },
  destroy: vi.fn().mockResolvedValue(undefined),
  off: vi.fn(),
  on: vi.fn(),
  seekTo: vi.fn().mockResolvedValue(undefined),
};

const createPlayer =
  vi.fn<(elementId: string, options: Record<string, unknown>) => Promise<typeof player>>();

Object.defineProperty(window, "Kinescope", {
  configurable: true,
  value: { IframePlayer: { create: createPlayer } },
});

describe("KinescopeInteractivePlayer", () => {
  beforeEach(() => {
    createPlayer.mockReset();
    player.destroy.mockReset().mockResolvedValue(undefined);
    player.off.mockReset();
    player.on.mockReset();
    player.seekTo.mockReset().mockResolvedValue(undefined);
  });

  afterEach(() => {
    consoleError.mockClear();
  });

  it("does not duplicate chapters into the SDK embed request", async () => {
    createPlayer.mockResolvedValue(player);

    render(
      <KinescopeInteractivePlayer
        videoId="video-with-chapters"
        chapters={[
          { id: "chapter-1", title: "Введение", startSeconds: 0 },
          { id: "chapter-2", title: "Резюме", startSeconds: 120 },
        ]}
      />,
    );

    await waitFor(() => {
      expect(createPlayer).toHaveBeenCalledOnce();
    });
    const createOptions = createPlayer.mock.calls.at(0)?.[1];
    expect(createOptions).toBeDefined();
    expect(createOptions).not.toHaveProperty("playlist");
    expect(createOptions).toMatchObject({
      url: "https://kinescope.io/embed/video-with-chapters",
    });
  });

  it("falls back to a direct iframe when SDK initialization fails", async () => {
    createPlayer.mockRejectedValue(new TypeError("Cross-origin request blocked"));

    render(<KinescopeInteractivePlayer videoId="video-fallback" startSeconds={42} />);

    const iframe = await screen.findByTitle("Видеоурок");
    expect(iframe).toHaveAttribute("src", "https://kinescope.io/embed/video-fallback?seek=42");
  });

  it("destroys a partially initialized SDK player before showing fallback", async () => {
    createPlayer.mockResolvedValue(player);
    player.on.mockImplementationOnce(() => {
      throw new TypeError("Subscription failed");
    });

    render(<KinescopeInteractivePlayer videoId="video-partial-failure" />);

    await screen.findByTitle("Видеоурок");
    await waitFor(() => {
      expect(player.destroy).toHaveBeenCalledOnce();
    });
  });

  it("seeks the direct iframe when an external chapter is selected", async () => {
    const playerHandle = createRef<KinescopeInteractivePlayerHandle>();
    createPlayer.mockRejectedValue(new TypeError("Cross-origin request blocked"));
    render(<KinescopeInteractivePlayer ref={playerHandle} videoId="video-fallback-seek" />);
    const initialIframe = await screen.findByTitle("Видеоурок");

    await act(async () => {
      await playerHandle.current?.seekTo(125);
    });

    const firstSeekIframe = screen.getByTitle("Видеоурок");
    expect(firstSeekIframe).not.toBe(initialIframe);
    expect(firstSeekIframe).toHaveAttribute(
      "src",
      "https://kinescope.io/embed/video-fallback-seek?seek=125",
    );

    await act(async () => {
      await playerHandle.current?.seekTo(125);
    });
    const repeatedSeekIframe = screen.getByTitle("Видеоурок");
    expect(repeatedSeekIframe).not.toBe(firstSeekIframe);

    await act(async () => {
      await playerHandle.current?.seekTo(0);
    });
    const zeroSeekIframe = screen.getByTitle("Видеоурок");
    expect(zeroSeekIframe).not.toBe(repeatedSeekIframe);
    expect(zeroSeekIframe).toHaveAttribute("src", "https://kinescope.io/embed/video-fallback-seek");
  });
});
