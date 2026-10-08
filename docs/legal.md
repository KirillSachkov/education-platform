# Юридические документы (152-ФЗ + ЗоЗПП)

Документы на сайте, их версии, cookie-banner, persistence согласий. Нужно только если
трогаешь legal markdown, версии, cookie-banner или `auth.user_consents`.

## Документы на сайте

5 документов из приватного `LEGAL_DOCUMENTS_DIR` рендерятся через `/legal/{slug}`:

- `/legal/offer` — Договор-оферта (информационно-консультационные услуги, НЕ образовательные по 273-ФЗ)
- `/legal/privacy` — Политика обработки ПДн
- `/legal/consent-pd` — Согласие на обработку ПДн (отдельный документ — требование 156-ФЗ от 24.06.2025)
- `/legal/cookies` — Политика cookies
- `/legal/consent-marketing` — Согласие на рекламные рассылки (38-ФЗ ст. 18)

Архивные версии — `/legal/{slug}/v{N}` (с пометкой о текущей версии). Реквизиты опубликованы в `frontend/src/widgets/site-footer/`.

## Версии — single source of truth в двух местах (sync обязателен)

- backend: `backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs`
- frontend: `frontend/src/shared/legal/versions.ts#CURRENT_LEGAL_VERSIONS`

Владелец утверждает новую редакцию отдельно от выпуска кода. После утверждения обнови
версию в обоих файлах и сохрани Markdown/PDF в приватном хранилище. Сохрани прежние
версии с исходными именами. Дата, которую ещё предстоит выбрать, пишется как `⟦…⟧`;
документ с таким плейсхолдером нельзя выпускать.

Публичный source не содержит юридических Markdown/PDF. Тест
`shared/legal/__tests__/current-documents.test.ts` проверяет загрузку приватных файлов,
версии и безопасные пути на синтетических fixtures. Он не проверяет production документы.
Оператор перед deploy запускает из `frontend/`:

```bash
LEGAL_DOCUMENTS_DIR=/srv/education-platform/private-legal/markdown \
LEGAL_PDFS_DIR=/srv/education-platform/private-legal/pdf \
node --experimental-strip-types scripts/validate-legal-assets.mjs
```

Проверка требует текущие Markdown/PDF, непустой текст без плейсхолдеров и PDF signature.
Она не устанавливает законность редакции, соответствие PDF тексту или право выпуска.
Оператор отдельно сверяет hashes с утверждённым выпуском, оба registry, текущие и
архивные URLs. Pending HEAD не означает, что владелец разрешил новую оферту.

## Приватные файлы в runtime

| Переменная | Default относительно frontend process directory | Файлы |
| --- | --- | --- |
| `LEGAL_DOCUMENTS_DIR` | `content/legal` | `<slug>-vN.md` |
| `LEGAL_PDFS_DIR` | `public/legal-docs` | `<slug>-vN.pdf` |

Приложение читает файлы при запросе. `/legal-docs/<slug>-vN.pdf` отдаёт исходный PDF.
При отсутствии файла URL возвращает 404. Неправильный mount не заменяется выдуманными
условиями. Docker build исключает обе директории; оператор предоставляет private
read-only mounts. Для контейнера можно указать `/run/legal/markdown` и `/run/legal/pdf`
в переменных и смонтировать туда соответствующие приватные host directories.
Для локального `npm run start` укажи абсолютные host paths. Генератор
`frontend/scripts/generate-legal-pdfs.sh` принимает те же переменные.

До приёма регистраций или оплат оператор проверяет утверждённые файлы, registry и
URLs. Не монтируй пустые каталоги поверх документов в legacy image: такой mount
скроет действующие условия. Архив и production configuration ведёт coordinator.

## Cookie-banner

`widgets/cookie-banner` + `shared/lib/use-cookie-consent` с категоризацией necessary/analytics/marketing. Я.Метрика (`widgets/yandex-metrika`) грузится только при analytics consent. Без `NEXT_PUBLIC_YANDEX_METRIKA_ID` молча не грузится (dev-friendly).

## Persistence согласий

`auth.user_consents` (см. [`backend/AuthService/CLAUDE.md`](../backend/AuthService/CLAUDE.md) раздел «User Consents»). Юр-доказательство при споре с РКН/потребителем — IP, User-Agent, версия документа, timestamp.

## Key files

| Looking for | File |
|---|---|
| Private Markdown/PDF | `LEGAL_DOCUMENTS_DIR` + `LEGAL_PDFS_DIR` (defaults выше) |
| Версии legal-документов (sync) | `backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs` + `frontend/src/shared/legal/versions.ts` |
| User consents persistence | `backend/AuthService/src/AuthService.Domain/UserConsent.cs`, миграция `AddUserConsents` |
| Cookie-banner widget | `frontend/src/widgets/cookie-banner/` + `frontend/src/shared/lib/use-cookie-consent.ts` |
| Site-footer с реквизитами ИП | `frontend/src/widgets/site-footer/` |
| Реквизиты ИП | `frontend/src/widgets/site-footer/` |
