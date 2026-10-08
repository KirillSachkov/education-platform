#!/usr/bin/env bash
# Генерирует PDF-версии всех юридических документов из content/legal/*.md
# в public/legal-docs/. Запускать локально после правки/версионирования документа
# (см. шаги версионирования в src/shared/legal/versions.ts).
#
# Требует: pandoc + tectonic (PDF-движок с поддержкой кириллицы).
#   brew install pandoc tectonic
#
# PDF кладутся под /legal-docs/, а НЕ под /legal/, чтобы не конфликтовать
# с динамическим роутом app/legal/[doc].
set -euo pipefail

cd "$(dirname "$0")/.."

SRC_DIR="${LEGAL_DOCUMENTS_DIR:-content/legal}"
OUT_DIR="${LEGAL_PDFS_DIR:-public/legal-docs}"

shopt -s nullglob
files=("$SRC_DIR"/*.md)
if (( ${#files[@]} == 0 )); then
  echo "No private legal Markdown found in $SRC_DIR" >&2
  exit 1
fi

mkdir -p "$OUT_DIR"

for md in "${files[@]}"; do
  base="$(basename "$md" .md)"   # e.g. offer-v1
  out="$OUT_DIR/$base.pdf"
  echo "→ $md → $out"
  pandoc "$md" -f markdown --pdf-engine=tectonic \
    -V mainfont="Times New Roman" \
    -V monofont="Menlo" \
    -V geometry:margin=2.5cm \
    -V fontsize=11pt \
    -V lang=ru \
    -o "$out"
done

echo "✓ Готово: $(ls -1 "$OUT_DIR"/*.pdf | wc -l | tr -d ' ') PDF в $OUT_DIR"
