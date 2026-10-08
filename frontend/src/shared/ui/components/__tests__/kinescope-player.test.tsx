import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { KinescopePlayer } from "../kinescope-player";

vi.mock("next/image", () => ({
  default: ({ alt }: { alt: string }) => <span role="img" aria-label={alt} />,
}));

describe("KinescopePlayer", () => {
  it("gives the embedded player an accessible title", () => {
    render(<KinescopePlayer videoId="video-1" />);

    expect(screen.getByTitle("Видеоурок")).toBeInTheDocument();
  });
});
