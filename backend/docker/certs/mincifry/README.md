# Корни НУЦ Минцифры для T-Bank TLS (#1000)

~03.08.2026 T-Bank переключил `securepay.tinkoff.ru` на цепочку
«Russian Trusted Sub CA → Russian Trusted Root CA» (НУЦ Минцифры). В базовых
образах `mcr.microsoft.com/dotnet/*` этого root нет, поэтому TLS-рукопожатие к
T-Bank API падало с `AuthenticationException: UntrustedRoot`.

Официальный root регистрируется в образе AccessService через
`update-ca-certificates` (см. `backend/AccessService/Dockerfile`). Live sub
хранится здесь только как reference для проверки цепочки и не устанавливается
как отдельный trust anchor.

## Провенанс

- `russian-trusted-root-ca.crt` — официальный корневой сертификат НУЦ Минцифры,
  скачан с gu-st.ru. sha256 fingerprint сверен с живой цепочкой:
  `D2:6D:2D:02:31:B7:C3:9F:92:CC:73:85:12:BA:54:10:35:19:E4:40:5D:68:B5:BD:70:3E:97:88:CA:8E:CF:31`
  (действителен 2022-03-01 → 2032-02-27).
- `russian-trusted-sub-ca.crt` — промежуточный CA, извлечён из живой цепочки
  `securepay.tinkoff.ru` (официально опубликованный sub отличается).
  sha256 fingerprint:
  `21:55:78:50:36:C9:00:DB:B5:F1:BB:2A:15:69:C8:0C:55:59:5B:D6:BF:94:86:7A:29:BB:DD:BC:7D:88:A3:F2`
  (действителен 2024-07-15 → 2029-07-19). Подпись проверена:
  `openssl verify -CAfile russian-trusted-root-ca.crt russian-trusted-sub-ca.crt` → OK.
  Сервер сам отдаёт этот промежуточный сертификат. Он завендорен для проверки
  live-цепочки, но не добавляется в системный trust store.

## Ротация

Если T-Bank снова сменит цепочку:

1. Снять живую цепочку:
   `openssl s_client -connect securepay.tinkoff.ru:443 -showcerts </dev/null`
2. Сверить root с публикацией на gu-st.ru по sha256 fingerprint (значение выше —
   эталон на момент #1000).
3. Заменить reference-файлы здесь, проверить `openssl verify`, пересобрать образ и
   убедиться, что внутри него
   `openssl s_client -connect securepay.tinkoff.ru:443 -verify_return_error`
   отвечает `Verify return code: 0 (ok)`.
