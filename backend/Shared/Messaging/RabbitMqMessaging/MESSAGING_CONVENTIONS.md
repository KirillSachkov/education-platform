# Соглашения По Именованию RabbitMQ

Этот файл является единым источником истины по именованию RabbitMQ в этом репозитории.

## 1) Обменник (Exchange)

Формат:

`<домен>.events`

Правила:

- Только нижний регистр.
- Сегменты разделяются точкой.
- Один exchange на один бизнес-домен.

Примеры:

- `education-content.events`
- `file.events`

## 2) Ключ Маршрутизации (Routing Key)

Формат:

`<сущность>.<действие>[.<цель>]`

Где:

- `сущность` — предмет события (`material`, `module`, `video`, `image`).
- `действие` — изменение жизненного цикла/состояния (`created`, `published`, `sent_to_draft`, `archived`, `access_changed`, `soft_deleted`, `hard_deleted`, `restored`).
- `цель` — необязательный динамический контекст (например, тип владеющей сущности: `material`, `course`, `module`).

Правила:

- Разделитель сегментов — `.`.
- Допустимые символы сегмента: строчные буквы, цифры, `_`, `-`.
- Сегмент не может начинаться или заканчиваться на `_` или `-`.

Примеры:

- `material.created`
- `material.hard_deleted`
- `video.created.material`
- `image.deleted.module`

Примеры wildcard-шаблонов:

- `*.created`
- `*.soft_deleted`
- `*.*.material`

## 3) Очередь (Queue)

Формат:

`<сервис_потребитель>.<домен_источник>.<цель_потребителя>_events`

Правила:

- Имя queue описывает назначение (intent) потребителя, а не одиночный тип события.
- Специфика событий задается в binding keys (`*.hard_deleted`, `*.created` и т.д.).
- Только нижний регистр.
- Внутри сегмента для составных слов используется `_`.

Примеры:

- `education.file.material_events`
- `education.file.module_events`
- `file.education.asset_cleanup_events`
- `file.education.asset_ownership`
- `progress.education.lifecycle_events`

## 4) Текущая Архитектура RabbitMQ (Как Есть)

Область:

- `EducationContentService`
- `FileService`
- `ProgressService`
- `AuthService`
- `AccessService` (Wolverine bus + `access.events` exchange wired in Phase G1)

Транспортные настройки по умолчанию для всех сервисов:

- `ExchangeType = Topic`
- `IsDurable = true`
- `AutoProvision()`
- `UseQuorumQueues()`
- `EnableWolverineControlQueues()`

### 4.1 Обменники и Сервисы-Публикаторы

| Обменник           | Сервис-публикатор         | Ключи маршрутизации                                                                                                                                                                                            |
| ------------------ | ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `education.events` | `EducationContentService` | `material.created`, `material.published`, `material.sent_to_draft`, `material.archived`, `material.access_changed`, `material.hard_deleted`, `issue.created`, `issue.access_changed`, `issue.soft_deleted`, `issue.restored`, `issue.hard_deleted`, `module.soft_deleted`, `module.hard_deleted`, `course.hard_deleted`, `course.asset_ownership_changed`, `collection.created`, `collection.updated`, `collection.published`, `collection.access_changed`, `collection.hard_deleted`, `quiz.published`, `quiz.access_changed`, `quiz.hard_deleted` |
| `file.events`      | `FileService`             | `file.bound.<target>`, `file.deleted.<target>` |
| `auth.events`      | `AuthService`             | `user.created`, `user.logged_in`, `user.username_updated`, `user.github_login`, `user.telegram_linked`, `user.telegram_unlinked`                                                                                |
| `progress.events`  | `ProgressService`         | `issue_submission.approved`, `issue_submission.changes_requested`, `issue_submission.awaiting_review` (payload `IssueSubmissionAwaitingReview`, PR-submission; `AiReviewRequested=false` значит author notification без ARS auto-run), `issue_submission.author_help_requested` (payload `IssueSubmissionAuthorHelpRequested`, #383 — студент нажал «Позвать автора»; consumer NotificationService → автору), `issue.author_question_asked` (payload `IssueAuthorQuestionAsked`, #693, publish-route добавлен в #1156 — студент задал приватный вопрос автору по заданию ДО сабмишена; consumer NotificationService → автору курса). (access-derive-model Phase 4 #367: `course_enrollment.created/revoked/restored` сняты — enrollment'ы стали lazy progress-anchor'ами без cross-service сигнала; доступ/уведомления/подписки целиком grant-driven.) |
| `notification.events` | `NotificationService` (`notification.created`, `notification.read`, `notification.broadcast_requested`) **и** `TelegramBotService` (`notification.telegram_delivery_recorded` — payload `TelegramDeliveryRecorded` со status + provider_message_id + error_code) | См. два события выше |
| `access.events`    | `AccessService`           | `plan_grant.created` (payload `PlanGrantCreated`, несёт denorm `plan_name` = `Plan.DisplayName` (#445) + `offer_type` = `Plan.OfferType` (#485 — consumer подбирает существительное «курс/интенсив/марафон» без callback'а)) — публикуется при redeem invite-link'а (`POST /access/invites/{token}/redeem`) и admin-grant'е (`POST /access/grants/admin/`); `plan_grant.revoked` (payload `PlanGrantRevoked`) — публикуется при revoke grant'а (`POST /access/grants/{id}/revoke`); `plan_grant.expired` (payload `PlanGrantExpired`) — публикуется `ExpiredGrantsSweeper` после effective boundary; `plan_course.bound` / `plan_course.unbound` — catalog-visibility lifecycle COURSE-плана; `plan.hard_deleted` — полное удаление плана; `trial.expiry_approaching` — reminder примерно за 5 дней до trial expiry (#580); subscription lifecycle (#746): `plan_grant.renewed`, `plan_grant.renewal_failed` (каждая definitive ошибка, retry/terminal stage), `plan_grant.renewal_cancelled`, `plan_grant.renewal_resumed`; canonical schedule `T−24h → T → T+48h`, hard grace `T+72h`; NotificationService обрабатывает renewed/failed/cancelled; `tg_join.reminder_requested` (payload `TgJoinReminderRequested{UserId, PlanId, GrantId, PlanName, Stage, OccurredAt}`) — нудж на вступление в TG-группу (#616). |
| `telegram.events`  | `TelegramBotService`      | `chat_binding.bound_to_plan`, `chat_binding.unbound_from_plan` — chat-binding lifecycle (#68); `chat_member.confirmed` (payload `ChatMemberConfirmed{PlatformUserId, PlanId, TelegramChatId, OccurredAt}`) — публикуется `ChatJoinRequestHandler` после approve'а `chat_join_request`'а в bound chat **И** `ChatMemberUpdateHandler` при вступлении через admin/auto-approve (#616 — закрывает дыру, где «X принят(а) в группу» через ручной аппрув не постил welcome и не подтверждал членство); один event на каждый matched plan, epic #397. |
| `assignment_review.events` | `AssignmentReviewService` | `ai_review.iteration.completed` (payload `AiReviewIterationCompleted`) — публикуется в конце каждой run-iteration в `RunIterationHandler`. Несёт verdict (пустой при failure'е), GitHubReviewId, IterationNumber. Phase 8 #15: ProgressService denorm-handler. `ai_review.queued_for_submission` (payload `AiReviewQueuedForSubmission`) — публикуется когда AiReview создан под submission и есть active installation; ProgressService гейтит `ReadyForHumanReview=false`. `vcs_installation.created` (payload `VcsInstallationCreated`) — issue #307: публикуется `CompleteInstallationHandler` после успешного install / re-install / reactivate GitHub App. Consumer — AccessService (`VcsInstallationCreatedHandler` auto-completes `GITHUB_REVIEW_APP` onboarding step). `ai_review.oversized_skipped` (payload `AiReviewOversizedSkipped`) — issue #546: авто-ран пропущен, reviewable diff выше hard-cap'ов; публикуется один раз на AiReview из `PersistFailedIterationAsync` (manual-run с `AllowOversizedDiff` не публикует). Consumer — NotificationService (уведомление автору курса «запустите проверку вручную»). `student_pr_question.asked` (payload `StudentPrQuestionAsked`) — issue #713: публикуется при reply/комменте студента-владельца PR (webhook `pull_request_review_comment` / `issue_comment` в `HandleGitHubWebhook`), идемпотентно по `github_comment_id`. Consumers — NotificationService (уведомление автору курса) + ProgressService (денорм `student_question_at` для бейджа в панели проверки). |

**Access subscription lifecycle (#746):** `plan_grant.renewal_failed` публикуется на каждой definitive failed attempt с `Stage=RETRY_SCHEDULED|TERMINAL_FAILURE`, order correlation, `NextRetryAt` и неизменяемым `GraceEndsAt`; consumer не вычисляет schedule сам. `plan_grant.renewal_cancelled` и `plan_grant.renewal_resumed` представляют реальные owner transitions и получают новый operation correlation на каждый переход (он остаётся стабильным при redelivery). NotificationService связывает renewed/failed/cancelled с `/my-plans`; resume отдельного пользовательского уведомления не создаёт.

`<target>` — динамический сегмент, берется из `TargetEntityType` (`material`, `course`).

### 4.2 Очереди и Подписки

| Очередь                                | Сервис-потребитель        | Обменник           | Ключи привязки                              | Доставляемые семейства событий                   |
| -------------------------------------- | ------------------------- | ------------------ | ------------------------------------------- | ------------------------------------------------ |
| `education_content.file.material_events` | `EducationContentService` | `file.events`      | `*.*.material`                              | файловые события для материалов (material_video, material_preview) |
| `education_content.file.course_events` | `EducationContentService` | `file.events`      | `*.*.course`                                | файловые события для курсов (course_preview, course_video) |
| `education_content.access.plan_course_events` | `EducationContentService` | `access.events` | `plan_course.bound`, `plan_course.unbound` | `PlanCourseEventsHandler` инвалидирует HybridCache `access:plan-for-course:{CourseId}` для актуальной цены в каталоге. |
| `education_content.content_access.sync` | `EducationContentService` | `education.events` | content access lifecycle events | self-consume authoritative access projection; `issue.created`/`issue.access_changed` rebuild issue tags from DB, `issue.hard_deleted` clears them. |
| `file.education.asset_cleanup_events`  | `FileService`             | `education.events` | `*.hard_deleted`                            | события жесткого удаления                        |
| `file.education.asset_ownership`       | `FileService`             | `education.events` | `course.asset_ownership_changed`             | монотонная desired-owner projection per target; replay/out-of-order отсекаются по revision, поздние insert/bind применяют projection под target advisory lock |
| `progress.education.lifecycle_events`  | `ProgressService`         | `education.events` | `course.created`, `issue.published`, `material.hard_deleted`, `module.hard_deleted`, `issue.hard_deleted`, `course.hard_deleted`, `quiz.hard_deleted` | только события с зарегистрированными ProgressService handlers; exact bindings не пропускают остальные lifecycle-события в очередь без обработчика |
| `access.content_access.sync`           | `AccessService` (self)    | `access.events`    | `plan_grant.created`, `plan_grant.revoked`, `plan_grant.expired`, `plan_grant.renewal_refunded` | self-consume: пишет plan-grant теги в Redis (`plan:all` для платформенного FULL_ALL/LEARN_ALL, `plan:course:{id}` для COURSE); revoke/expire/refunded renewal запускают authoritative recalc через `PlanGrantTagCalculator` + atomic Redis `MULTI/EXEC` replace, сохраняя overlap других grants. (`plan:free:author_X` тег удалён в #358 — FREE collapsed в systemwide REGISTERED; `plan:lifetime:author_X` только legacy parser.) |
| `access.plan_onboarding.grant_events`  | `AccessService` (self)    | `access.events`    | `plan_grant.created`                        | self-consume: создаёт `user_plan_onboardings` строку при получении plan-grant'а если у плана `plan_onboarding_flows.is_enabled=true` и есть шаги. Идемпотентно — повторный grant того же плана пользователю не пере-создаёт state (#68). |
| `access.plan_onboarding.telegram_events` | `AccessService`         | `telegram.events`  | `chat_binding.bound_to_plan`, `chat_binding.unbound_from_plan` | auto-sync TELEGRAM step в onboarding flow: на bind ensure'ит, на unbind — removes (если flow.IsEnabled). Подписан на новый exchange `telegram.events`, publish'имый TelegramBotService (#68). |
| `access.plan_onboarding.telegram_member_events` | `AccessService`  | `telegram.events`  | `chat_member.confirmed`                     | epic #397: `ChatMembershipConfirmedHandler` — на подтверждённое членство в bound chat'е auto-complete'ит pending TELEGRAM onboarding step для (user, plan) + продвигает курсор. Идемпотентно (no-op если уже completed / нет онбординга / нет TELEGRAM step'а). Отдельная очередь от `access.plan_onboarding.telegram_events`, чтобы два флоу не делили binding-set. **+ #616:** sibling `ChatMemberConfirmedTgJoinHandler` на этой же очереди завершает `tg_join_reminders` (user, plan) → останавливает напоминания на вступление. |
| `access.plan_onboarding.vcs_events` | `AccessService` | `assignment_review.events` | `vcs_installation.created` | issue #307: `VcsInstallationCreatedHandler` auto-completes `GITHUB_REVIEW_APP` onboarding step во всех active onboardings юзера после install AI-review GitHub App. Идемпотентно. |
| `access.tg_join_reminder.grant_events` | `AccessService` (self) | `access.events` | `plan_grant.created` | #616: `PlanGrantCreatedTgJoinHandler` — на community-grant с привязанным чатом создаёт `tg_join_reminders` row + publish'ит `tg_join.reminder_requested` (стадия `INITIAL`). Гейт: `COMMUNITY_ACCESS` cap (из `evt.Capabilities`) + у плана есть active chat-binding (`ITelegramBotServiceClient.HasActiveChatBindingAsync`) + не `MIGRATION`/self-grant. Идемпотентно по (user, plan). Отдельная очередь от `grant_events`/`onboarding`. |
| `notifications.access.tg_join_reminder_events` | `NotificationService` | `access.events` | `tg_join.reminder_requested` | #616: `TgJoinReminderRequestedHandler` — нудж на вступление в TG-группу. `INITIAL` → только InApp (F1 invite-DM уже покрывает Telegram на момент гранта); `REMINDER_1`/`REMINDER_2` → InApp+Telegram+Email (out-of-band до непривязавших Telegram; Telegram авто-фильтруется dispatcher'ом если TG не привязан). Idempotency per (GrantId, stage). Deep-link `/telegram/join?plan=`. |
| `access.auth.github_events`            | `AccessService`           | `auth.events`      | `user.github_login`                         | issue #65/#69 — `UserGithubLoginAccessHandler` мэтчит orgs пользователя против активных планов с `github_org_slug` и идемпотентно выпускает PlanGrant (Source=GITHUB_ORG). |
| `notifications.progress.submission_events` | `NotificationService` | `progress.events` | `issue_submission.approved`, `issue_submission.changes_requested`, `issue_submission.awaiting_review`, `issue_submission.author_help_requested`, `issue.author_question_asked` | author/student review-нотификации. `issue_submission.author_help_requested` (#383) → `IssueSubmissionAuthorHelpRequestedHandler` шлёт автору курса `AuthorHelpRequested`-уведомление «Студенту нужна помощь». `issue.author_question_asked` (#693) → `IssueAuthorQuestionAskedHandler` шлёт автору курса `IssueAuthorQuestion`-уведомление с вопросом студента по заданию (issue-scoped, ДО сабмишена). |
| `notifications.comments.thread_events` | `NotificationService`     | `comment.events`   | `comment.created`                           | событие создания комментария → до 2-х нотификаций (`CommentReplied` автору parent'а, `CommentOnOwnContent` владельцу сущности) |
| `notifications.education.content_events` | `NotificationService`   | `education.events` | `material.published`, `issue.published`     | новый материал/задание в курсе → уведомление подписчикам курса (`MaterialPublished`/`IssuePublished` notifications). Автор может снять чекбокс «Уведомить подписчиков» в publish dialog (`NotifySubscribers=false` → handler выходит сразу). |
| `notifications.education.course_created` | `NotificationService`   | `education.events` | `course.created`                            | fan-out auto-subscribe для платформенных FULL_ALL/LEARN_ALL grantee'ов (issue #80). При создании нового курса автомагически создаёт `Subscription(COURSE, newCourseId)` для каждого юзера с активным `plan:all` grant'ом, чтобы они получали последующие `material.published`. Route клиента ещё передаёт `authorId` как legacy context, AccessService его не использует для фильтрации полного доступа. |
| `notifications.access.grant_events`    | `NotificationService`     | `access.events`    | `plan_grant.created`                        | три sibling-хендлера на одной очереди: `PlanGrantReceivedHandler` (уведомление получателю «доступ открыт», `PlanGrantReceived`), `SubscribeOnPlanGrantCreatedHandler` (fan-out подписок на покрытые курсы), `PlanGrantAuthorSaleHandler` (#428 — уведомление АВТОРУ плана о новом участнике с данными покупателя имя/@ник/email + раздельно «План/курс» из `plan_name` и «Доступ» из tier'а, #445; пропускает `Source=MIGRATION` и self-grant). |
| `notifications.access.trial_reminder_events` | `NotificationService` | `access.events` | `trial.expiry_approaching` | `TrialExpiryApproachingHandler` (#580) — напоминание пользователю «пробный доступ скоро истекает, доплати до полного» (InApp + Telegram + Email — email-канал добавлен в #687; correlation = `GrantId`, deep-link `/pricing`). Отдельная очередь от `grant_events` (другой recipient + lifecycle). |
| `notifications.access.expiry_events` | `NotificationService` | `access.events` | `plan_grant.expired` | `AccessExpiredHandler` (#687) — пользователю «доступ закончился, продлите» ПОСЛЕ истечения time-limited grant'а по TTL (InApp + Telegram + Email, correlation = `GrantId`, deep-link `/pricing`). `PlanGrantExpired` без `PlanName` → описание из `PlanTier`. `plan_grant.revoked` сознательно НЕ консьюмится (refund/admin-revoke сбивал бы CTA «зачтётся оплаченное»). Отдельная очередь от `trial_reminder_events` (post-expiry lifecycle). |
| `notifications.assignment_review.review_events` | `NotificationService` | `assignment_review.events` | `ai_review.oversized_skipped`, `student_pr_question.asked` | issue #546: `AiReviewOversizedSkippedHandler` шлёт автору курса `AiReviewOversizedSkipped`-уведомление «Большой PR — запустите AI-проверку вручную» (InApp + Telegram, correlation = AiReviewId). issue #713: `StudentPrQuestionAskedHandler` уведомляет автора курса о вопросе студента в PR (InApp + Telegram, correlation = StudentPrMessageId, deep-link `/author/review`). |
| `comments.education.lifecycle_events`  | `CommentService`          | `education.events` | `material.hard_deleted`, `course.hard_deleted`, `issue.hard_deleted`, `quiz.hard_deleted` | exact bindings; каскадное hard-delete всех комментариев, включая soft-deleted, для удалённого target'а |
| `telegram_bot.notifications.delivery_events` | `TelegramBotService` | `notification.events` | `notification.created`                  | доставка in-app нотификаций в Telegram-личку, если в `Channels` запрошен Telegram-bit |
| `telegram_bot.auth.user_events`        | `TelegramBotService`      | `auth.events`      | `user.telegram_unlinked`, `user.telegram_linked` | удаление локального `UserLink` (unlinked) + re-trigger F1 invite DM на linked     |
| `telegram_bot.access.plan_deleted_events` | `TelegramBotService`   | `access.events`    | `plan.hard_deleted`                         | issue #417 — `PlanHardDeletedTelegramHandler` отвязывает (удаляет) chat_binding'и полностью удалённого плана + best-effort revoke invite-link. Отдельная очередь от F1/F6 grant-обработки. |
| `notifications.self.telegram_delivery_events` | `NotificationService` | `notification.events` | `notification.telegram_delivery_recorded` | результат TG-доставки от TelegramBotService → запись в `notification_deliveries` (channel=Telegram). Closes слепое пятно по доставке Telegram-уведомлений (до этого `notification_deliveries` не содержал записей с channel=2) |
| `assignment_review.education.review_context_snapshot` | `AssignmentReviewService` | `education.events` | `project.review_context.updated` | `StoreProjectGuidelinesHandler` снапшотит guidelines в `project_review_guidelines` для PromptBuilder'а (Phase 7 #15). RAG-ref-repo indexing удалён в #320 — рядом с этой очередью больше нет parallel `IndexProjectRefRepoHandler`. |
| `assignment_review.education.review_spec`    | `AssignmentReviewService` | `education.events` | `issue.review_spec.updated`      | `StoreIssueReviewSpecHandler` снапшотит ReviewSpec (author prompt + review aspects + reference links) в `issue_review_specs` для PromptBuilder'а (Phase 7 #15). |
| `assignment_review.progress.submission_awaiting_review` | `AssignmentReviewService` | `progress.events` | `issue_submission.awaiting_review` | `IssueSubmissionAwaitingReviewHandler` создаёт AiReview под GitHub PR submission только при `AiReviewRequested=true`. Если есть active installation — публикует `AiReviewQueuedForSubmission`; иначе AiReview создаётся в FAILED со специальным failure_reason. При `AiReviewRequested=false` handler no-op'ит: author notification остаётся в NotificationService, AI не запускается. Phase 8 #15. |
| `assignment_review.education.cleanup` | `AssignmentReviewService` | `education.events` | `issue.hard_deleted` | `IssueHardDeletedAssignmentReviewHandler` каскадно сносит `ai_reviews` + `ai_review_iterations` (FK CASCADE) + `issue_review_specs` для удалённого задания. Production hardening #15; context_documents cleanup убран в #320 вместе с RAG. |
| `progress.assignment_review.denorm`          | `ProgressService`         | `assignment_review.events` | `ai_review.iteration.completed`, `ai_review.queued_for_submission`, `student_pr_question.asked` | `AiReviewIterationCompletedHandler` пишет 4 денорм-поля (`LatestAiVerdict` + `AiIterationsCount` + `LastAiIterationAt` + `AiReviewStatus`) на `IssueSubmission`. `AiReviewQueuedForSubmissionHandler` гейтит `ReadyForHumanReview=false`. Phase 8 #15. issue #713: `StudentPrQuestionAskedHandler` пишет `student_question_at` денорм на `IssueSubmission` (питает бейдж «новый вопрос от студента» в панели `/author/review`). |

### 4.3 Сквозная карта коммуникации

- `EducationContentService` публикует доменные события жизненного цикла в `education.events`.
- `FileService` подписан на `education.events` по `*.hard_deleted` и выполняет очистку файловых ресурсов.
- `ProgressService` подписан на `education.events` только по exact keys, для которых есть handlers: `course.created`, `issue.published` и hard-delete material/module/issue/course/quiz.
- `FileService` публикует медиа-события в `file.events` с динамическим целевым сегментом.
- `EducationContentService` подписан на `file.events` для целей `material` и `course` и обновляет ссылки на медиа в материалах/курсах.
- `AuthService` публикует пользовательские события в `auth.events`.
- `CommentService` публикует `comment.created` в `comment.events` через durable outbox (envelope schema `comments`). `NotificationService` потребляет это событие в очереди `notifications.comments.thread_events` и диспатчит до двух нотификаций: `CommentReplied` автору parent-комментария (если это ответ) и `CommentOnOwnContent` владельцу сущности (резолв через `IEducationContentServiceClient.GetEntityOwnershipAsync`). Self-thread (author = parent author / owner = author) фильтруется в handler'е.
- `AccessService` имеет durable outbox (envelope schema `access`) и зарегистрированный exchange `access.events`. Routing keys `plan_grant.created` / `plan_grant.revoked` / `plan_grant.expired` публикуются из use-case'ов и фоновой задачи: `RedeemInviteHandler`, `AdminGrantHandler`, `StartTrialHandler` → `plan_grant.created`; `RevokeGrantHandler` → `plan_grant.revoked`; `ExpiredGrantsSweeper` → `plan_grant.expired`; `TrialExpiryReminderSweeper` → `trial.expiry_approaching` (#580); `RecurringChargesSweeper` → `plan_grant.renewed` / `plan_grant.renewal_failed` (#614).
- `PlanGrantRenewalRefunded` публикуется как `plan_grant.renewal_refunded` после атомарного отката полного renewal-refund (#748). `access.content_access.sync` пересчитывает Redis/GitHub entitlement, а `telegram_bot.access.grant_events` перед F6 kick проверяет оставшиеся active grants. Обе очереди имеют exact binding на новый routing key.
- `ProgressService` **больше не подписан** на `access.events` (access-derive-model Phase 4 #367 удалил очередь `progress.access.grant_events` + handler'ы `PlanGrantCreatedHandler`/`PlanGrantRevokedHandler`). Enrollment'ы не материализуются на grant — они создаются лениво как progress-anchor'ы при первом engagement'е (`EnsureEnrollmentAsync`, entitlement-gated). Доступ синкается в Redis self-consume handler'ами AccessService; уведомления и подписки — `NotificationService` на `plan_grant.created`. Per-course `CourseEnrolled`/`CourseEnrollmentRevoked`/`CourseEnrollmentRestored` события и доменная цепочка `GrantContentAccessOnEnrollment`/`RevokeContentAccessOnSuspension`/`RestoreContentAccessOnActivation` удалены.

### 4.4 Текущие риски топологии

- `progress.education.lifecycle_events` не потребляет `*.hard_deleted` напрямую; жесткое удаление обрабатывается отдельными handler-ами в ProgressService (`MaterialHardDeletedHandler`, `ModuleHardDeletedHandler`, `IssueHardDeletedHandler`, `CourseHardDeletedHandler`, `QuizHardDeletedHandler`), подписанными на соответствующие routing keys.

## 5) Архитектурные ограничения

- Не включать окружение (`dev`, `prod`) в имена.
- По умолчанию не кодировать версию в именах обменников/очередей.
- По возможности оставлять динамический сегмент в конце ключа маршрутизации.

