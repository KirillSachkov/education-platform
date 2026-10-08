"use client";

import type { EntityType } from "@/shared/config/entity-types";
import { useStore } from "zustand";
import { createStore } from "zustand/vanilla";

export type GlobalSearchEntityType = "All" | EntityType;

/**
 * Scope определяет, где ищем:
 * - `everywhere` — по всей платформе (по всем авторам и курсам)
 * - `author` — в пределах текущего пространства автора (резолвится из URL)
 * - `course` — в пределах текущего курса (если пользователь в курсовой навигации)
 *
 * `null` = пользователь ещё не делал выбор; компонент подставит дефолт по контексту
 * (в курсе → course, в пространстве → author, иначе → everywhere). Как только
 * `setScope` вызван — store помнит явный выбор и больше не пересчитывает дефолт.
 *
 * Фактический authorId/courseId прокидывается в SearchDocumentsFilters из компонента,
 * который знает текущий route-контекст. Store хранит только выбор пользователя.
 */
export type GlobalSearchScope = "everywhere" | "author" | "course";

export interface GlobalSearchTagPayload {
  id: string;
  title: string;
}

interface GlobalSearchState {
  open: boolean;
  query: string;
  search: string;
  selectedEntityType: GlobalSearchEntityType;
  selectedTags: GlobalSearchTagPayload[];
  scope: GlobalSearchScope | null;
}

interface GlobalSearchActions {
  openSearch: () => void;
  closeSearch: () => void;
  setQuery: (query: string) => void;
  setSearch: (search: string) => void;
  setSelectedEntityType: (entityType: GlobalSearchEntityType) => void;
  setScope: (scope: GlobalSearchScope) => void;
  replaceSelectedTags: (tags: GlobalSearchTagPayload[]) => void;
  addSelectedTag: (tag: GlobalSearchTagPayload) => void;
  removeSelectedTag: (tagId: string) => void;
  openWithTag: (tag: GlobalSearchTagPayload) => void;
}

type GlobalSearchStore = GlobalSearchState & GlobalSearchActions;

const GLOBAL_SEARCH_INPUT_SELECTOR = '[data-global-search-input="true"]';

const initialState: GlobalSearchState = {
  open: false,
  query: "",
  search: "",
  selectedEntityType: "All",
  selectedTags: [],
  scope: null,
};

const globalSearchStore = createStore<GlobalSearchStore>()((set) => ({
  ...initialState,
  openSearch: () => set({ open: true }),
  closeSearch: () => set({ open: false }),
  setQuery: (query) => set({ query }),
  setSearch: (search) => set({ search }),
  setSelectedEntityType: (selectedEntityType) => set({ selectedEntityType }),
  setScope: (scope) => set({ scope }),
  replaceSelectedTags: (selectedTags) => set({ selectedTags }),
  addSelectedTag: (tag) =>
    set((state) => ({
      selectedTags: state.selectedTags.some((item) => item.id === tag.id)
        ? state.selectedTags
        : [...state.selectedTags, tag].sort((left, right) =>
            left.title.localeCompare(right.title, "ru", { sensitivity: "base" }),
          ),
    })),
  removeSelectedTag: (tagId) =>
    set((state) => ({
      selectedTags: state.selectedTags.filter((tag) => tag.id !== tagId),
    })),
  openWithTag: (tag) =>
    set({
      open: true,
      query: "",
      search: "",
      selectedEntityType: "All",
      selectedTags: [tag],
    }),
}));

function focusGlobalSearchInput() {
  if (typeof window === "undefined") {
    return;
  }

  window.requestAnimationFrame(() => {
    document
      .querySelector<HTMLInputElement>(GLOBAL_SEARCH_INPUT_SELECTOR)
      ?.focus();
  });
}

export function useGlobalSearchStore<T>(selector: (state: GlobalSearchStore) => T) {
  return useStore(globalSearchStore, selector);
}

export function requestGlobalSearchOpen() {
  globalSearchStore.getState().openSearch();
  focusGlobalSearchInput();
}

export function requestGlobalSearchTag(tag: GlobalSearchTagPayload) {
  globalSearchStore.getState().openWithTag(tag);
  focusGlobalSearchInput();
}
