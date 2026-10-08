#!/bin/bash
# ESLint per-rule warning ratchet (#712).
# Ошибки (severity=error) блокируют всегда. Warn-правила (strictTypeChecked с
# долгом, список в frontend/eslint.config.mjs ESLINT_RATCHET_RULES) сверяются
# с baseline: рост по любому правилу = fail, снижение = подсказка ужать baseline.
# Локальное обновление baseline после выжигания долга: scripts/ci/eslint-ratchet.sh --update
set -uo pipefail
cd "$(dirname "$0")/../../frontend"

BASELINE=".eslint-warnings-baseline.json"
REPORT="${TMPDIR:-/tmp}/eslint-ratchet-report.json"

LINT_TYPED=1 npx eslint -f json -o "$REPORT" . || true
[ -f "$REPORT" ] || { echo "eslint-ratchet: ESLint не создал отчёт (фатальная ошибка конфига?) — смотри вывод выше"; exit 1; }

MODE="${1:-check}"
python3 - "$BASELINE" "$REPORT" "$MODE" <<'PY'
import json, sys, collections

baseline_path, report_path, mode = sys.argv[1], sys.argv[2], sys.argv[3]
report = json.load(open(report_path))

errors = []
warns = collections.Counter()
for f in report:
    for m in f["messages"]:
        if m["severity"] == 2:
            errors.append(f'{f["filePath"]}:{m.get("line", "?")} [{m.get("ruleId")}] {m["message"]}')
        elif m["severity"] == 1 and m.get("ruleId"):
            warns[m["ruleId"]] += 1

if errors:
    print(f"ESLint: {len(errors)} ОШИБОК (блокируют всегда):")
    print("\n".join(errors[:50]))
    print("Fix: исправь перечисленные ошибки (файл:строка выше).")
    sys.exit(1)

if mode == "--update":
    json.dump(dict(sorted(warns.items())), open(baseline_path, "w"), indent=2, ensure_ascii=False)
    print(f"Baseline обновлён: {baseline_path} ({sum(warns.values())} warnings по {len(warns)} правилам)")
    sys.exit(0)

try:
    base = json.load(open(baseline_path))
except FileNotFoundError:
    print(f"Baseline {baseline_path} не найден. Создай локально: bash scripts/ci/eslint-ratchet.sh --update")
    sys.exit(1)

grew = {r: (c, base.get(r, 0)) for r, c in warns.items() if c > base.get(r, 0)}
if grew:
    print("ESLint ratchet FAIL — новые нарушения ratchet-правил (долг может только падать):")
    for r, (c, b) in sorted(grew.items()):
        print(f"  {r}: {c} > baseline {b} (+{c - b})")
    print("Fix: найди СВОИ новые нарушения в изменённых файлах: cd frontend && LINT_TYPED=1 npx eslint <files>. Чужой долг чинить не нужно.")
    sys.exit(1)

shrunk = {r: (warns.get(r, 0), b) for r, b in base.items() if warns.get(r, 0) < b}
if shrunk:
    print("Долг уменьшился — ужми baseline (локально: bash scripts/ci/eslint-ratchet.sh --update, закоммить):")
    for r, (c, b) in sorted(shrunk.items()):
        print(f"  {r}: {c} < {b}")
print(f"ESLint ratchet OK ({sum(warns.values())} warnings ≤ baseline)")
PY
