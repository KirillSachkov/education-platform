# Shared local runtime lease

Read this operational reference before any command that starts, rebuilds, migrates, or stops the
shared Docker Compose runtime. The short command-time guard lives in root `AGENTS.md`; this file is
the canonical lifecycle and recovery procedure.

Docker Compose runtime общий для всех worktree и агентов проекта: на хосте допускается только один stack.

## Владение

До `up-*`, `down`, rebuild, migration или любой другой команды, которая запускает, пересоздаёт либо останавливает Compose-сервисы, получи lease или handoff от текущего владельца runtime. Нельзя параллельно поднимать второй Compose project или менять порты.

Если runtime занят, не запускай Docker-зависимые проверки и не меняй его состояние. Отложи их до handoff; shell-, static- и unit-проверки без Docker продолжай выполнять.

## Lease lifecycle

Каноническая, неотслеживаемая запись lease общая для всех worktree:

```bash
lease="$(git rev-parse --git-common-dir)/local-runtime.lease"
```

1. **Status.** Перед Docker-операцией проверь `test -e "$lease"`: отсутствует — runtime свободен; существует — прочитай owner metadata и не изменяй stack без handoff. Пустая, неполная или нечитаемая запись считается занятой и требует corrupt recovery, а не нового acquire.
2. **Acquire.** Сначала полностью подготовь metadata во временном файле внутри `git-common-dir`, затем атомарно опубликуй его hard link по каноническому пути. `ln` обязан завершиться ошибкой, если lease уже существует; нельзя сначала создавать пустой `$lease`, а потом дописывать metadata.

   ```bash
   git_common_dir="$(git rev-parse --git-common-dir)"
   lease="$git_common_dir/local-runtime.lease"
   lease_tmp="$(mktemp "${git_common_dir}/local-runtime.lease.tmp.XXXXXX")" || exit 1
   cleanup_lease_tmp() { rm -f -- "$lease_tmp"; }
   trap cleanup_lease_tmp EXIT
   trap 'exit 1' HUP INT TERM

   if ! printf 'owner=%s\ntask=%s\nworktree=%s\npid=%s\ncommand=%s\nacquired_at=%s\n' \
     "$USER" '<issue-or-task>' "$PWD" "$$" '<planned-command>' \
     "$(date '+%Y-%m-%dT%H:%M:%S%z')" > "$lease_tmp"; then
     printf 'failed to prepare runtime lease metadata\n' >&2
     exit 1
   fi

   if ! ln "$lease_tmp" "$lease" 2>/dev/null; then
     printf 'runtime lease is already held: %s\n' "$lease" >&2
     exit 1
   fi

   cleanup_lease_tmp
   trap - EXIT HUP INT TERM
   ```

3. **Handoff.** Только текущий `owner` может передать lease. Он явно согласует получателя, проверяет свою текущую metadata, полностью готовит новый файл в том же `git-common-dir` с `owner`/`task`/`worktree`/`pid`/`command`, `handoff_from` и `handoff_at`, затем делает atomic handoff заменой `$lease` через rename. Получатель сначала читает уже опубликованную запись и только затем продолжает работу как новый owner. Нельзя передавать lease удалением с последующим acquire: это создаёт окно для гонки.
4. **Release.** Только записанный `owner` освобождает lease после Docker-проверки; сначала сверяет metadata со своей задачей и процессом, затем удаляет `$lease`.
5. **Stale/corrupt recovery.** Возраст записи сам по себе не доказывает stale. До recovery подтверди, что записанные task и process больше не активны. Пустой, повреждённый lease или missing metadata не разрешают молча удалить запись: проверь доступный контекст runtime/worktree, явно зафиксируй отсутствующие поля и причину (`stale_recovery_reason`) в task report, затем удали подтверждённо stale/corrupt запись и выполни atomic acquire через подготовленный файл и hard link. Если acquire не удался, lease уже получил другой owner и stack менять нельзя. Без подтверждения и записанной причины runtime остаётся занятым.

## Обычная разработка

После получения lease подними только инфраструктуру и запусти нужный backend локально через
`dotnet run --project backend/<Service>/src/<Service>.Web`:

```bash
./scripts/dev.sh up-infra
```

Режимы `up`, `up-fe`, `up-all` и `up-obs` поднимают весь stack для интеграционной проверки. Любая
команда `dev.sh`, которая запускает, пересоздаёт или останавливает сервисы (`up*`, `migrate`,
`down`, `restore`), меняет общий runtime и требует `acquire or receive handoff lease`; одного
ожидания, пока runtime станет «свободен», недостаточно.

На общем runtime допускается только один frontend dev process: либо локальный `npm run dev`, либо Docker frontend из `up-fe` или `up-all`, но не оба одновременно.

Не вызывай глобальный `dotnet build-server shutdown`: он затрагивает другие задачи.
