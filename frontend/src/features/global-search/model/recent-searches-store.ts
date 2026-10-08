"use client";

import { useSyncExternalStore } from "react";

const STORAGE_KEY = "ep.recent-searches";
const MAX_ITEMS = 10;
const MIN_LENGTH = 2;

type Listener = () => void;
const listeners = new Set<Listener>();

function readFromStorage(): readonly string[] {
  if (typeof window === "undefined") return EMPTY;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY;
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return EMPTY;
    const cleaned: string[] = [];
    for (const item of parsed) {
      if (typeof item === "string" && item.length > 0 && item.length <= 200) {
        cleaned.push(item);
      }
      if (cleaned.length >= MAX_ITEMS) break;
    }
    return cleaned;
  } catch {
    return EMPTY;
  }
}

const EMPTY: readonly string[] = Object.freeze([]);
let snapshot: readonly string[] = EMPTY;
let initialized = false;

function ensureInitialized() {
  if (!initialized && typeof window !== "undefined") {
    snapshot = readFromStorage();
    initialized = true;
  }
}

function writeAndNotify(next: readonly string[]) {
  snapshot = next;
  if (typeof window !== "undefined") {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // quota exceeded or localStorage disabled — ignore
    }
  }
  for (const listener of listeners) {
    listener();
  }
}

// Единственный глобальный storage-listener на модуль. Каждый subscribe() не создаёт
// свой listener (это приводило бы к N обработчиков при монтировании нескольких
// потребителей + к race-condition на общий snapshot).
let storageListenerAttached = false;

function attachStorageListenerOnce() {
  if (storageListenerAttached || typeof window === "undefined") return;
  storageListenerAttached = true;
  window.addEventListener("storage", (event) => {
    if (event.key !== STORAGE_KEY) return;
    snapshot = readFromStorage();
    for (const listener of listeners) {
      listener();
    }
  });
}

function subscribe(listener: Listener) {
  ensureInitialized();
  attachStorageListenerOnce();
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function addRecentSearch(query: string) {
  ensureInitialized();
  const trimmed = query.trim();
  if (trimmed.length < MIN_LENGTH) return;
  const filtered = snapshot.filter(
    (item) => item.localeCompare(trimmed, undefined, { sensitivity: "accent" }) !== 0,
  );
  const next = [trimmed, ...filtered].slice(0, MAX_ITEMS);
  writeAndNotify(next);
}

export function removeRecentSearch(query: string) {
  ensureInitialized();
  const next = snapshot.filter((item) => item !== query);
  writeAndNotify(next);
}

export function clearRecentSearches() {
  writeAndNotify(EMPTY);
}

export function useRecentSearches(): readonly string[] {
  return useSyncExternalStore(
    subscribe,
    () => snapshot,
    () => EMPTY,
  );
}
