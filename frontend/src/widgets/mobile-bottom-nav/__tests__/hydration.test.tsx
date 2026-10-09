import { act } from "@testing-library/react";
import { hydrateRoot } from "react-dom/client";
import { renderToString } from "react-dom/server";
import { expect, it, vi } from "vitest";
import type * as Motion from "framer-motion";
const preference = vi.hoisted(() => ({ reduced: false }));
vi.mock("next/navigation", () => ({ usePathname: () => "/home" }));
vi.mock("framer-motion", async (importOriginal) => ({
  ...(await importOriginal<typeof Motion>()),
  useReducedMotion: () => preference.reduced,
}));
import { MobileTabTransition } from "../ui/mobile-tab-transition";
it("hydrates when the browser requests reduced motion", async () => {
  const content = (
    <MobileTabTransition>
      <p>Learning</p>
    </MobileTabTransition>
  );
  preference.reduced = false;
  const container = document.createElement("div");
  container.innerHTML = renderToString(content);
  document.body.append(container);
  preference.reduced = true;
  const onRecoverableError = vi.fn();
  let root: ReturnType<typeof hydrateRoot>;
  await act(() => {
    root = hydrateRoot(container, content, { onRecoverableError });
    return Promise.resolve();
  });
  expect(onRecoverableError).not.toHaveBeenCalled();
  expect(container.textContent).toBe("Learning");
  act(() => {
    root.unmount();
  });
  container.remove();
});
