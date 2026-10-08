import { createStore } from "zustand/vanilla";

type AuthStatus = "loading" | "authenticated" | "unauthenticated";

interface TokenState {
  accessToken: string | undefined;
  error: string | undefined;
  status: AuthStatus;
  setTokenState: (
    accessToken: string | undefined,
    error: string | undefined,
    status: AuthStatus,
  ) => void;
}

export const tokenStore = createStore<TokenState>((set) => ({
  accessToken: undefined,
  error: undefined,
  status: "loading",
  setTokenState: (accessToken, error, status) =>
    set({ accessToken, error, status }),
}));

/** Promise that resolves when auth leaves "loading" state */
export function waitForAuth(): Promise<void> {
  if (tokenStore.getState().status !== "loading") return Promise.resolve();
  return new Promise((resolve) => {
    const unsub = tokenStore.subscribe((state) => {
      if (state.status !== "loading") {
        unsub();
        resolve();
      }
    });
  });
}
