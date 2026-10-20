# Stored material artifacts

MaterialProcessingService no longer runs transcription, chapter generation, text generation,
material AI model administration, usage queries or jobs. Shared/AI remains the AssignmentReviewService
dependency for review generation, progress events and model usage accounting. The existing
`/admin/ai-models` route retains AssignmentReview settings; its MaterialProcessing section is removed.

## Storage and read dependencies

| Artifact | Durable storage | Active read |
| --- | --- | --- |
| Lesson text / ready summary | `education.materials.content` | EducationContentService material detail, existing editor |
| Ready chapters | `education.materials.chapter_titles` / `chapter_timestamps` | Material detail and chapter player; existing manual chapter update remains |
| Raw transcript segments and metadata | `files.video_transcripts` | FileService SRT endpoint and admin MCP exports |
| Historical source transcript rows | `material_processing.video_transcripts` | One-time copy migration only; no active endpoint reads this schema |

`20261009080000_PreserveCompletedVideoTranscripts` creates the FileService retained-artifact table
and copies **all** source rows when the source table exists. It preserves IDs, video IDs, every
asset version, orphan rows, duration, language, timestamps and JSONB values. It performs no
reserialization, filtering or deletion. A missing legacy schema is valid for a fresh installation;
a present but incompatible table fails the migration transaction. The old table remains intact.
The table is read through Dapper and is deliberately outside the EF aggregate model; the
FileService model and snapshot are unchanged. Downgrade is blocked to prevent artifact loss.

`GET /videos/{videoId}/subtitles.srt/` requires `Videos.MANAGE` and the same owner/admin check as
the former endpoint. Anonymous users and other owners cannot download raw text. It reads the
current `VideoProviderRef.Version`, rather than the binding revision or asset version. Missing
transcripts and transcripts for a previous provider version return 404 without starting jobs.
SRT uses stored start/end seconds, legacy field fallback, ascending start order, trimmed text,
milliseconds and total hours. MCP exports call `/api/videos/{videoId}/subtitles.srt/` with their
existing authenticated client; they do not bypass the API boundary.

`20261009090000_CleanupRetainedVideoTranscriptsOnAssetDeletion` preserves the former
`FileAssetDeleted` transcript cleanup. A FileService database trigger removes every transcript
version for the exact VIDEO asset when its deletion state is saved, or when its row is purged.
Endpoint deletion, target lifecycle deletion, slot replacement and retention all persist this
same state. Cleanup therefore shares their database transaction and rolls back with failed
deletion; repeat deletion is safe. Other assets and non-video assets remain untouched. Existing
video provider/tombstone retention policy remains in force; this migration does not delete
provider media or introduce another maintenance job. The initial copy still preserves all rows.

## Future operational gate

Source removal does not stop deployed containers or retire schemas, queues or data. Before a
future operational rollout:

1. Capture the old service source SHA and immutable migrations in a private source archive.
2. Stop and drain the old MPS service, its producers and queued/running jobs. Prove that no
   transcript writer remains and row counts/checksums stay stable. Generic Compose startup or
   migration does not prove this: an obsolete container can remain alive outside the new topology.
3. Take a fresh private archive of the entire `material_processing` schema and data, including
   raw segments. Record checksums, row counts and a consistent backup point after writes stopped.
4. Restore that archive in isolation. Compare every transcript column and every version with
   the source; prove usable SRT export and preserved material text/chapters. A synthetic source-free
   rehearsal validates tooling; it does not replace this future backup/restore gate.
5. Apply the FileService copy migration. Compare complete row/metadata/segment checksums between
   old and new tables, including orphan rows and older versions. Run retained SRT owner/admin
   and denied-access probes before enabling retained reads.
6. Keep the original schema, archives and frozen role manifests for rollback. Schema/data
   retirement needs a separate reviewed backup/restore gate and owner command.

The obsolete bindings are `material_processing.education.cleanup`,
`material_processing.file.cleanup`, `material_processing.file.ready` and
`notifications.material_processing.failure_events`; the exchange is `material_processing.events`.
Their removal from source topology does not delete broker state. Queue/exchange retirement belongs
to the same later operational gate. No cleanup, production backfill or deploy occurs here.

The FileService integration suite exercises fresh schema creation, fail-closed upgrade, full
synthetic row fidelity, isolated `pg_dump`/`pg_restore`, blocked downgrade and access boundaries.
Its fixtures contain no production source data. Original MaterialProcessing migrations remain
byte-identical at their historical paths.
