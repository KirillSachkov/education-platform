#!/bin/bash
# tsc strict-flag ratchet (#712): noUncheckedIndexedAccess + exactOptionalPropertyTypes
# живут в frontend/tsconfig.strict.json, долг зафиксирован per-file в
# frontend/.ts-strict-baseline.json и может только уменьшаться.
# Выжег файл до нуля — он исчезает из baseline (--update); выжег флаг целиком —
# перенеси его в tsconfig.json и удали из tsconfig.strict.json.
# Локальное обновление: scripts/ci/ts-strict-ratchet.sh --update
set -uo pipefail
cd "$(dirname "$0")/../../frontend"

BASELINE=".ts-strict-baseline.json"
OUT="${TMPDIR:-/tmp}/ts-strict-out.txt"

npx tsc --noEmit -p tsconfig.strict.json > "$OUT" 2>&1 || true

MODE="${1:-check}"
python3 - "$BASELINE" "$OUT" "$MODE" <<'PY'
import json, re, sys, collections

baseline_path, out_path, mode = sys.argv[1], sys.argv[2], sys.argv[3]
counts = collections.Counter()
for line in open(out_path, encoding="utf-8", errors="replace"):
    m = re.match(r"(.+?)\(\d+,\d+\): error TS\d+", line)
    if m:
        counts[m.group(1)] += 1

if mode == "--update":
    json.dump(dict(sorted(counts.items())), open(baseline_path, "w"), indent=2, ensure_ascii=False)
    print(f"Baseline обновлён: {baseline_path} ({sum(counts.values())} ошибок в {len(counts)} файлах)")
    sys.exit(0)

try:
    base = json.load(open(baseline_path))
except FileNotFoundError:
    print(f"Baseline {baseline_path} не найден. Создай локально: bash scripts/ci/ts-strict-ratchet.sh --update")
    sys.exit(1)

grew = {f: (c, base.get(f, 0)) for f, c in counts.items() if c > base.get(f, 0)}
if grew:
    print("ts-strict ratchet FAIL — новые нарушения строгих tsc-флагов (долг может только падать):")
    for f, (c, b) in sorted(grew.items()):
        print(f"  {f}: {c} > baseline {b} (+{c - b})")
    print("Fix: cd frontend && npx tsc --noEmit -p tsconfig.strict.json — почини ошибки в СВОИХ изменённых файлах (обычно undefined-проверка после индексации).")
    sys.exit(1)

shrunk = {f: (counts.get(f, 0), b) for f, b in base.items() if counts.get(f, 0) < b}
if shrunk:
    print("Долг уменьшился — ужми baseline (локально: bash scripts/ci/ts-strict-ratchet.sh --update, закоммить):")
    for f, (c, b) in sorted(shrunk.items()):
        print(f"  {f}: {c} < {b}")
print(f"ts-strict ratchet OK ({sum(counts.values())} ошибок ≤ baseline)")
PY
