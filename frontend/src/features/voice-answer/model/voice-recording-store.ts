"use client";

/**
 * Персист голосовых черновиков ответа (#585) в IndexedDB. Blob нельзя положить в
 * localStorage, поэтому держим записи в IDB по ключу `session:item` — чтобы запись
 * пережила навигацию между вопросами, смену вкладки и перезагрузку страницы.
 * Чистится на «Удалить» и после успешной отправки ответа (раннер). SSR-safe — без
 * `indexedDB` все операции тихо no-op'ят.
 */
const DB_NAME = "trainer-voice";
const STORE = "recordings";

function openDb(): Promise<IDBDatabase | null> {
  return new Promise((resolve) => {
    if (typeof indexedDB === "undefined") {
      resolve(null);
      return;
    }
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => resolve(null);
  });
}

/** Ключ записи по сессии и item'у. */
export function voiceRecordingKey(sessionId: string, itemId: string): string {
  return `${sessionId}:${itemId}`;
}

export async function saveVoiceRecording(key: string, blob: Blob): Promise<void> {
  const db = await openDb();
  if (!db) return;
  await new Promise<void>((resolve) => {
    const tx = db.transaction(STORE, "readwrite");
    tx.objectStore(STORE).put(blob, key);
    tx.oncomplete = () => resolve();
    tx.onerror = () => resolve();
    tx.onabort = () => resolve();
  });
  db.close();
}

export async function loadVoiceRecording(key: string): Promise<Blob | null> {
  const db = await openDb();
  if (!db) return null;
  const result = await new Promise<Blob | null>((resolve) => {
    const tx = db.transaction(STORE, "readonly");
    const req = tx.objectStore(STORE).get(key);
    req.onsuccess = () => resolve(req.result instanceof Blob ? req.result : null);
    req.onerror = () => resolve(null);
  });
  db.close();
  return result;
}

export async function deleteVoiceRecording(key: string): Promise<void> {
  const db = await openDb();
  if (!db) return;
  await new Promise<void>((resolve) => {
    const tx = db.transaction(STORE, "readwrite");
    tx.objectStore(STORE).delete(key);
    tx.oncomplete = () => resolve();
    tx.onerror = () => resolve();
    tx.onabort = () => resolve();
  });
  db.close();
}
