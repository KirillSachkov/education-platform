"use client";

export default function GlobalError({
  error: _error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <html lang="ru">
      <body>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            minHeight: "100svh",
            fontFamily: "system-ui, sans-serif",
          }}
        >
          <div style={{ textAlign: "center" }}>
            <h1 style={{ fontSize: "1.5rem", fontWeight: 700, marginBottom: "0.5rem" }}>
              Критическая ошибка
            </h1>
            <p style={{ color: "#71717a", marginBottom: "1rem" }}>
              Приложение столкнулось с непредвиденной ошибкой.
            </p>
            <button
              onClick={reset}
              style={{
                padding: "0.5rem 1rem",
                background: "#18181b",
                color: "#fafafa",
                border: "none",
                borderRadius: "0.375rem",
                cursor: "pointer",
              }}
            >
              Перезагрузить
            </button>
          </div>
        </div>
      </body>
    </html>
  );
}
