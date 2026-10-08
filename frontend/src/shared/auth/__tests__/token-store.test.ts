import { describe, it, expect, beforeEach } from "vitest";
import { tokenStore, waitForAuth } from "../token-store";

describe("tokenStore", () => {
  beforeEach(() => {
    // Reset store to initial state before each test
    tokenStore.getState().setTokenState(undefined, undefined, "loading");
  });

  it("has correct initial state", () => {
    const state = tokenStore.getState();
    expect(state.accessToken).toBeUndefined();
    expect(state.error).toBeUndefined();
    expect(state.status).toBe("loading");
  });

  it("setTokenState updates all fields", () => {
    tokenStore
      .getState()
      .setTokenState("my-token", undefined, "authenticated");

    const state = tokenStore.getState();
    expect(state.accessToken).toBe("my-token");
    expect(state.error).toBeUndefined();
    expect(state.status).toBe("authenticated");
  });

  it("setTokenState can set error state", () => {
    tokenStore
      .getState()
      .setTokenState(undefined, "Token expired", "unauthenticated");

    const state = tokenStore.getState();
    expect(state.accessToken).toBeUndefined();
    expect(state.error).toBe("Token expired");
    expect(state.status).toBe("unauthenticated");
  });
});

describe("waitForAuth", () => {
  beforeEach(() => {
    tokenStore.getState().setTokenState(undefined, undefined, "loading");
  });

  it("resolves immediately if status is not loading", async () => {
    tokenStore
      .getState()
      .setTokenState("token", undefined, "authenticated");

    await expect(waitForAuth()).resolves.toBeUndefined();
  });

  it("resolves when status changes from loading to authenticated", async () => {
    const promise = waitForAuth();

    // Simulate auth completion
    tokenStore
      .getState()
      .setTokenState("new-token", undefined, "authenticated");

    await expect(promise).resolves.toBeUndefined();
  });

  it("resolves when status changes from loading to unauthenticated", async () => {
    const promise = waitForAuth();

    tokenStore
      .getState()
      .setTokenState(undefined, "No session", "unauthenticated");

    await expect(promise).resolves.toBeUndefined();
  });
});
