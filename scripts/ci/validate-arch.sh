#!/bin/bash
# Архитектурные инварианты как механическая проверка (#712).
# Правила, которые раньше жили прозой в CLAUDE.md:
#   1. Per-service Directory.Build.props = ТОЛЬКО <Import> корневого файла.
#      Самостоятельный <PropertyGroup> через MSBuild "nearest wins" молча
#      отключает ВСЕ анализаторы сервиса (реальный инцидент: MaterialProcessingService, #712).
#   2. Cross-service ProjectReference — только на *.Contracts.csproj.
#      (Shared/**, external/** и внутрисервисные ссылки — свободно.)
#   3. {Service}.Domain не ссылается на Core/Infrastructure/Web/Contracts
#      своего сервиса — домен внизу слоёв.
set -uo pipefail
cd "$(dirname "$0")/../.."

python3 - <<'PY'
import pathlib, re, sys

backend = pathlib.Path("backend")
fails = []

# --- Rule 1: per-service Directory.Build.props must import the root file ---
# Additive overrides рядом с Import (например <NoWarn>$(NoWarn);NU1510</NoWarn>)
# безопасны; фейл — отсутствие Import либо переопределение analyzer-критичных
# свойств (они молча ослабляют строгость для всего сервиса).
CRITICAL = ("TreatWarningsAsErrors", "AnalysisMode", "AnalysisLevel",
            "EnforceCodeStyleInBuild", "EnableNETAnalyzers", "Nullable",
            "CodeAnalysisTreatWarningsAsErrors")
for props in sorted(backend.glob("*/Directory.Build.props")):
    text = props.read_text(encoding="utf-8", errors="replace")
    if "<Import" not in text:
        fails.append(
            f"{props}: нет <Import> корневого Directory.Build.props — MSBuild 'nearest wins' "
            f"молча отключает все анализаторы сервиса (инцидент: MaterialProcessingService, #712).\n"
            f"  Fix: замени содержимое на единственный <Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))\" />"
        )
        continue
    overridden = [p for p in CRITICAL if f"<{p}>" in text]
    if overridden:
        fails.append(
            f"{props}: переопределяет analyzer-критичные свойства {overridden} — "
            f"ослабляет строгость для всего сервиса.\n"
            f"  Fix: убери их (наследуются из корневого props); точечные исключения — в .globalconfig с комментарием."
        )

# --- Rules 2+3: ProjectReference graph ---
SERVICE_DIRS = {p.name for p in backend.iterdir()
                if p.is_dir() and p.name not in ("Shared", "external") and list(p.glob("**/*.csproj"))}

ref_re = re.compile(r'<ProjectReference\s+Include="([^"]+)"')
for csproj in sorted(backend.glob("**/*.csproj")):
    if "external" in csproj.parts or "obj" in csproj.parts:
        continue
    parts = csproj.relative_to(backend).parts
    svc = parts[0] if parts[0] in SERVICE_DIRS else None
    text = csproj.read_text(encoding="utf-8", errors="replace")
    for ref in ref_re.findall(text):
        target = (csproj.parent / pathlib.PureWindowsPath(ref).as_posix()).resolve()
        try:
            t_parts = target.relative_to(backend.resolve()).parts
        except ValueError:
            continue  # ссылка за пределы backend (не наш инвариант)
        t_svc = t_parts[0] if t_parts[0] in SERVICE_DIRS else None

        # Rule 2: cross-service refs only via *.Contracts
        if svc and t_svc and t_svc != svc and not target.name.endswith(".Contracts.csproj"):
            fails.append(
                f"{csproj}: cross-service ссылка на {target.name} ({t_svc}).\n"
                f"  Fix: межсервисное взаимодействие — только через {t_svc}.*.Contracts "
                f"(typed HTTP client) или RabbitMQ events, не прямой ProjectReference."
            )

        # Rule 3: Domain stays at the bottom of the service layers
        if svc and ".Domain.csproj" in csproj.name and t_svc == svc:
            for layer in (".Core.csproj", ".Web.csproj", ".Contracts.csproj"):
                if target.name.endswith(layer):
                    fails.append(
                        f"{csproj}: Domain ссылается на {target.name} — домен не должен "
                        f"зависеть от верхних слоёв.\n  Fix: инвертируй зависимость (interface в Domain)."
                    )
            if ".Infrastructure" in target.name:
                fails.append(
                    f"{csproj}: Domain ссылается на {target.name} — домен не должен "
                    f"зависеть от инфраструктуры.\n  Fix: инвертируй зависимость (interface в Domain)."
                )

if fails:
    print(f"validate-arch: {len(fails)} нарушений архитектурных инвариантов:\n")
    print("\n\n".join(fails))
    sys.exit(1)
print("validate-arch: OK (props-import, cross-service-via-Contracts, Domain-at-bottom)")
PY
