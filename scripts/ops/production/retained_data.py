"""Preserve retired rows before migrations and selectively rehydrate legacy schemas.

The caller owns the deployment lock, quiesces every application writer and runs
the recorded EF Down bundles before rehydration. This module never restores an
old database snapshot over current application data.
"""

import json
import re

TABLES = (
    "auth.author_spaces", "education.quizzes", "education.course_quizzes",
    "education.collection_items", "education.module_items", "education.materials",
    "education.roadmaps", "education.roadmap_nodes", "education.roadmap_edges",
    "education.short_links", "progress.level_test_attempts", "progress.progress_users",
    "progress.user_gamification_stats", "progress.xp_awards",
    "progress.course_certificates", "progress.material_notes",
)
SCHEMAS = ("access", "assignment_review", "auth", "comments", "education", "files",
           "material_processing", "notifications", "progress", "search", "tags", "telegrambot", "trainer")
BASELINES = {
    "auth-service": ("auth", "20260710173325_AddAvatarBindingRevision"),
    "education-service": ("education", "20260711212217_AddCommentTargetOwnershipContractV1"),
    "progress-service": ("progress", "20260706150113_AddStudentQuestionAt"),
}


def identifier(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]{0,62}", value):
        raise ValueError("ordinary SQL identifier required")
    return '"' + value + '"'


def literal(value):
    return "'" + value.replace("'", "''") + "'"


def qualified(table):
    return ".".join(identifier(part) for part in table.split("."))


def columns(query, schemas=SCHEMAS):
    scope = ",".join(literal(schema) for schema in schemas)
    rows = json.loads(query("SELECT coalesce(json_agg(row_to_json(x)),'[]') FROM ("
        "SELECT table_schema,table_name,json_agg(column_name ORDER BY ordinal_position) AS columns "
        "FROM information_schema.columns WHERE table_schema IN (" + scope + ") "
        "GROUP BY 1,2 ORDER BY 1,2) x;"))
    return {row["table_schema"] + "." + row["table_name"]: row["columns"] for row in rows}


def row_fingerprints(query, selections):
    statements = []
    for table, selection in selections.items():
        statements.append("SELECT " + literal(table) + ",count(*),"
            "md5(coalesce(string_agg(md5(row_text),'' ORDER BY md5(row_text)),'')) "
            "FROM (" + selection + ") rows;")
    rows = query("\n".join(statements)).splitlines()
    result = {}
    for row in rows:
        table, count, digest = row.split("|")
        if table in result or table not in selections or not re.fullmatch(r"[0-9a-f]{32}", digest):
            raise ValueError("invalid data fingerprint result")
        result[table] = {"count": int(count), "row_hash": digest}
    if set(result) != set(selections):
        raise ValueError("incomplete data fingerprints")
    return result


def fingerprints(query, definitions, sources=None):
    selections = {}
    for table, names in definitions.items():
        selected = ",".join(identifier(name) for name in names)
        source = qualified((sources or {}).get(table, table))
        selections[table] = "SELECT row_to_json(x)::text AS row_text FROM (SELECT " + selected + " FROM " + source + ") x"
    return row_fingerprints(query, selections)


def core_fingerprints(query, definitions, plan):
    """Normalize only retired fields/rows; all current user writes stay exact."""
    retired = "SELECT id FROM " + qualified(archive_sources(plan["archive_schema"])["education.quizzes"]) + " WHERE purpose='LEVEL_TEST'"
    dropped = set(TABLES) - {"auth.author_spaces", "education.quizzes", "education.materials",
                            "education.course_quizzes", "education.collection_items", "education.module_items"}
    selections = {}
    for table, names in definitions.items():
        if table in dropped or table.endswith(".__EFMigrationsHistory") or table == "education.comment_target_ownership_v1":
            continue
        selected = ",".join(identifier(name) for name in names)
        expression, condition = "to_jsonb(x)", "TRUE"
        if table == "auth.author_spaces":
            expression += " || jsonb_build_object('feature_flags',feature_flags - 'Leaderboard' - 'leaderboard' - 'Roadmaps' - 'roadmaps')"
        elif table == "education.materials":
            expression += " || jsonb_build_object('quiz_id',CASE WHEN quiz_id IN (" + retired + ") THEN NULL ELSE quiz_id END)"
        elif table == "education.quizzes":
            condition = "purpose IS DISTINCT FROM 'LEVEL_TEST'"
        elif table == "education.course_quizzes":
            condition = "quiz_id IS NULL OR quiz_id NOT IN (" + retired + ")"
        elif table in {"education.collection_items", "education.module_items"}:
            kind = "QUIZ" if table == "education.collection_items" else "Quiz"
            condition = "(item_type=" + literal(kind) + " AND reference_id IN (" + retired + ")) IS NOT TRUE"
        selections[table] = "SELECT (" + expression + ")::text AS row_text FROM (SELECT " + selected + " FROM " + qualified(table) + ") x WHERE " + condition
    return row_fingerprints(query, selections)


def archive_sources(schema):
    if not re.fullmatch(r"retained_[a-z0-9_]{1,48}", schema):
        raise ValueError("dedicated retained schema required")
    return {table: schema + "." + table.replace(".", "__") for table in TABLES}


def preserve(query, schema):
    sources = archive_sources(schema)
    available = columns(query)
    if not set(TABLES).issubset(available) or "level_test_config" not in available["education.quizzes"]:
        raise ValueError("complete pre-retirement schema required")
    for namespace, expected in BASELINES.values():
        actual = query('SELECT "MigrationId" FROM ' + qualified(namespace + ".__EFMigrationsHistory")
                       + ' ORDER BY "MigrationId" DESC LIMIT 1;').strip()
        if actual != expected:
            raise ValueError("pre-retirement migration baseline changed")
    definitions = {table: available[table] for table in TABLES}
    before = fingerprints(query, definitions)
    statements = ["BEGIN ISOLATION LEVEL REPEATABLE READ;", "CREATE SCHEMA " + identifier(schema) + ";",
                  "REVOKE ALL ON SCHEMA " + identifier(schema) + " FROM PUBLIC;"]
    for table, destination in sources.items():
        original, copy = qualified(table), qualified(destination)
        statements += ["CREATE TABLE " + copy + " (LIKE " + original + " INCLUDING ALL);",
                       "INSERT INTO " + copy + " SELECT * FROM " + original + ";",
                       "DO $$ BEGIN IF EXISTS ((SELECT to_jsonb(x) FROM " + original + " x EXCEPT ALL SELECT to_jsonb(x) FROM " + copy + " x) UNION ALL "
                       "(SELECT to_jsonb(x) FROM " + copy + " x EXCEPT ALL SELECT to_jsonb(x) FROM " + original + " x)) "
                       "THEN RAISE EXCEPTION 'retained row comparison failed'; END IF; END $$;"]
    statements.append("COMMIT;")
    query("\n".join(statements))
    plan = {"schema_version": 1, "archive_schema": schema, "columns": definitions, "fingerprints": before}
    verify_archive(query, plan)
    if fingerprints(query, definitions) != before:
        raise ValueError("retired source changed during preservation")
    return plan


def verify_archive(query, plan):
    if type(plan.get("schema_version")) is not int or plan["schema_version"] != 1:
        raise ValueError("unknown retained-data plan")
    sources = archive_sources(plan["archive_schema"])
    if set(plan.get("columns", {})) != set(TABLES) or set(plan.get("fingerprints", {})) != set(TABLES):
        raise ValueError("complete retained-data plan required")
    if fingerprints(query, plan["columns"], sources) != plan["fingerprints"]:
        raise ValueError("retained data differs from pre-migration snapshot")


def rehydrate(query, plan):
    verify_archive(query, plan)
    for schema, expected in BASELINES.values():
        actual = query('SELECT "MigrationId" FROM ' + qualified(schema + ".__EFMigrationsHistory") + ' ORDER BY "MigrationId" DESC LIMIT 1;').strip()
        if actual != expected:
            raise ValueError("recorded EF Down must complete before data rehydration")
    available = columns(query)
    if any(available.get(table) is None or set(available[table]) != set(plan["columns"][table]) for table in TABLES):
        raise ValueError("recorded legacy EF schema must be restored before data")
    sources = archive_sources(plan["archive_schema"])
    retired_quizzes = "SELECT id FROM " + qualified(sources["education.quizzes"]) + " WHERE purpose='LEVEL_TEST'"

    def insert(table, condition="TRUE"):
        names = plan["columns"][table]
        selected = ",".join(identifier(name) for name in names)
        projected = ",".join("a." + identifier(name) for name in names)
        return "INSERT INTO " + qualified(table) + " (" + selected + ") SELECT " + projected + " FROM " + qualified(sources[table]) + " a WHERE " + condition + " ON CONFLICT DO NOTHING;"

    sql = ["BEGIN;", insert("education.quizzes", "a.purpose='LEVEL_TEST'"),
        "UPDATE education.quizzes t SET level_test_config=a.level_test_config FROM " + qualified(sources["education.quizzes"]) + " a WHERE t.id=a.id AND t.level_test_config IS NULL AND a.level_test_config IS NOT NULL;",
        "UPDATE auth.author_spaces t SET feature_flags=t.feature_flags || coalesce((SELECT jsonb_object_agg(key,value) FROM jsonb_each(a.feature_flags) WHERE key IN ('Leaderboard','leaderboard','Roadmaps','roadmaps') AND NOT t.feature_flags ? key),'{}'::jsonb) FROM " + qualified(sources["auth.author_spaces"]) + " a WHERE a.id=t.id;",
        "UPDATE education.materials t SET quiz_id=a.quiz_id FROM " + qualified(sources["education.materials"]) + " a WHERE t.id=a.id AND t.quiz_id IS NULL AND t.updated_at=a.updated_at AND a.quiz_id IN (" + retired_quizzes + ");",
        insert("education.course_quizzes", "a.quiz_id IN (" + retired_quizzes + ")"),
        insert("education.collection_items", "a.item_type='QUIZ' AND a.reference_id IN (" + retired_quizzes + ") AND (a.section_id IS NULL OR EXISTS(SELECT 1 FROM education.collection_sections s WHERE s.id=a.section_id))"),
        insert("education.module_items", "a.item_type='Quiz' AND a.reference_id IN (" + retired_quizzes + ") AND EXISTS(SELECT 1 FROM education.modules m WHERE m.id=a.module_id)"),
        insert("education.roadmaps"), insert("education.roadmap_nodes"), insert("education.roadmap_edges"),
        insert("education.short_links", "EXISTS(SELECT 1 FROM education.materials m WHERE m.id=a.material_id)"),
        insert("progress.level_test_attempts"), insert("progress.progress_users"), insert("progress.user_gamification_stats"),
        insert("progress.xp_awards", "a.enrollment_id IS NULL OR EXISTS(SELECT 1 FROM progress.course_enrollments e WHERE e.id=a.enrollment_id)"),
        insert("progress.course_certificates"), insert("progress.material_notes"), "COMMIT;"]
    query("\n".join(sql))
    verify_archive(query, plan)
