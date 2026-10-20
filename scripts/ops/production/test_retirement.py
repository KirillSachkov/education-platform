import copy
import json
from unittest.mock import Mock, patch

from closed_sales import protect_nginx
from environment_file import connection_database
import retained_data
from remote_production import write_json
from release_model import LEGACY_REGISTRY, PUBLIC_REGISTRY
from test_remote_production import RemoteFixture


class RetirementGuards(RemoteFixture):
    def test_unknown_sql_identifiers_archives_and_incomplete_fingerprints_fail(self):
        for name in ["public.foo", "../secret", 'x";DROP SCHEMA auth;', "", "a" * 64]:
            with self.subTest(name=name), self.assertRaises(ValueError): retained_data.identifier(name)
        for name in ["public", "retained_x;DROP", "retained_" + "a" * 49]:
            with self.subTest(name=name), self.assertRaises(ValueError): retained_data.archive_sources(name)
        for result in ["", "auth.users|1|" + "f" * 32 + "\nauth.users|2|" + "f" * 32,
                       "foreign.users|1|" + "f" * 32, "auth.users|1|not-a-hash"]:
            with self.subTest(result=result), self.assertRaises(ValueError):
                retained_data.fingerprints(lambda sql: result, {"auth.users": ["id"]})

    def test_preservation_refuses_missing_tables_or_advanced_baseline_before_copy(self):
        for absent in [True, False]:
            rows = [{"table_schema": table.split(".")[0], "table_name": table.split(".")[1],
                     "columns": ["id", "level_test_config"]} for table in retained_data.TABLES]
            if absent: rows.pop()
            query = Mock(side_effect=[json.dumps(rows), "unexpected-migration"])
            with self.subTest(absent=absent), self.assertRaises(ValueError): retained_data.preserve(query, "retained_fixture")
            self.assertFalse(any("CREATE SCHEMA" in call.args[0] for call in query.call_args_list))

    def test_public_to_legacy_without_recovery_is_not_an_image_only_rollback(self):
        self.host.current_record = {"registry": PUBLIC_REGISTRY}
        self.host.target = {"registry": LEGACY_REGISTRY, "source_sha": "a" * 40}
        self.host.inputs = {"operation": "rollback"}
        self.host.pending_path = self.root / "releases/pending.env"
        with self.assertRaisesRegex(ValueError, "recovery evidence missing"): self.host.prepare_retirement()

    def test_failure_before_archive_still_requires_ingress_and_worker_closure(self):
        self.host.current_record = {"registry": LEGACY_REGISTRY}
        self.host.target = {"registry": LEGACY_REGISTRY, "source_sha": "a" * 40}
        self.host.inputs = {"operation": "rollback"}
        self.host.pending_path = self.root / "releases/pending.env"
        self.host.pending_path.write_text("IMAGE_TAG=" + "b" * 40 + "\n")
        write_json(self.host.state / "pending.json", {"registry": PUBLIC_REGISTRY})
        self.host.prepare_retirement()
        self.assertTrue(self.host.sales_closed_rollback)
        self.assertFalse(self.host.retirement_rollback)

    def test_another_legacy_release_cannot_consume_a_recovery_plan(self):
        self.host.target = {"registry": LEGACY_REGISTRY, "source_sha": "b" * 40}
        self.host.inputs = {"operation": "rollback"}
        self.host.pending_path = self.root / "releases/pending.env"
        write_json(self.host.state / "retirement-recovery.json", {"schema_version": 1, "legacy_source_sha": "a" * 40})
        with self.assertRaisesRegex(ValueError, "exact recorded legacy"): self.host.prepare_retirement()

    def test_source_gates_fail_before_writers_stop_or_migrations_run(self):
        for failure in ["uid", "drain", "post-drain-backup", "preserve"]:
            host = copy.copy(self.host); events = []
            def event(name):
                events.append(name)
                if name == failure: raise RuntimeError("synthetic gate failure")
            host.baseline = lambda: event("baseline")
            def choose(): host.target = {"registry": PUBLIC_REGISTRY}
            host.choose_target = choose
            host.prepare_retirement = lambda: setattr(host, "retirement_transition", True)
            host.normal_runtime = lambda: {}
            host.pending_path = self.root / "releases/pending.env"
            for method, name in [("prepare_env", "env"), ("prepare_configuration", "configuration"),
                                 ("backup_runtime", "runtime"), ("pull", "pull"), ("verify_runtime_container", "uid"),
                                 ("preserve_or_recover", "preserve"), ("migrate_and_start", "migrate")]:
                setattr(host, method, lambda name=name: event(name))
            host.backup = lambda: event("post-drain-backup" if host.quiesced else "online-backup")
            def drain(): event("drain"); host.quiesced = True
            host.quiesce_retirement = drain
            with self.subTest(failure=failure), self.assertRaises(RuntimeError): host.execute()
            self.assertNotIn("migrate", events)
            if failure == "uid": self.assertNotIn("drain", events)
            if failure == "post-drain-backup": self.assertNotIn("preserve", events)
            self.assertLess(events.index("online-backup"), events.index("pull"))

    def test_non_stopped_writer_or_foreign_service_prevents_database_work(self):
        self.host.retirement_transition = True
        self.host.target = {"registry": PUBLIC_REGISTRY, "metadata": {"IMAGE_TAG": "b" * 40}, "postgres_image": self.host.roles["postgres_image"]}
        self.host.current_record = {"registry": LEGACY_REGISTRY}
        self.host.env.update(POSTGRES_USER="fixture", POSTGRES_PASSWORD="fixture", AUTH_DB_CONNECTION="Database=fixture;",
                             EDUCATION_DB_CONNECTION="Database=fixture;", PROGRESS_DB_CONNECTION="Database=fixture;")
        with patch.object(self.host, "database_recovery") as database:
            with self.assertRaisesRegex(ValueError, "writers did not stop"): self.host.quiesce_retirement()
            database.return_value.no_writers.assert_not_called()
        original = self.host.inspect
        def foreign(name):
            row = original(name); row["Config"]["Labels"]["com.docker.compose.project"] = "foreign"; return row
        self.host.calls.clear()
        with patch.object(self.host, "inspect", side_effect=foreign), self.assertRaisesRegex(ValueError, "foreign application"):
            self.host.quiesce_retirement()
        self.assertFalse(any(arguments[:2] == ["docker", "stop"] for arguments, _ in self.host.calls))

    def test_closed_sales_transform_is_idempotent_and_rejects_unknown_markup(self):
        source = b"events {}\nhttp {\n    server {\n        listen 80;\n    }\n    server {\n        listen 443 ssl;\n    }\n}\n"
        result = protect_nginx(source)
        self.assertEqual(protect_nginx(result), result)
        self.assertIn(b"orders/?$", result)
        self.assertIn(b"resume-renewal/?$", result)
        for raw in [b"http {}", b"http {\nhttp {\n", source + b"maintenance_sales_closed"]:
            with self.subTest(raw=raw), self.assertRaises(ValueError): protect_nginx(raw)

    def test_unknown_parallel_access_instance_stops_cutover(self):
        with patch.object(self.host, "command", return_value=b"fixture-access-service\nforeign-replica\n"):
            with self.assertRaisesRegex(ValueError, "another AccessService"): self.host.verify_access_instances()

    def test_application_database_comes_from_real_service_connections_not_optional_postgres_db(self):
        self.host.target = {"postgres_image": self.host.roles["postgres_image"]}
        self.host.env.update(POSTGRES_USER="fixture", POSTGRES_PASSWORD="fixture")
        self.host.env.pop("POSTGRES_DB", None)
        for key in ["AUTH_DB_CONNECTION", "EDUCATION_DB_CONNECTION", "PROGRESS_DB_CONNECTION"]:
            self.host.env[key] = 'Host=postgres;Password="semi;Database=decoy;quote";Database=applied_db;'
        self.assertEqual(self.host.database_recovery().database, "applied_db")
        self.host.env["PROGRESS_DB_CONNECTION"] = "Database=another_db;"
        with self.assertRaisesRegex(ValueError, "identities differ"): self.host.database_recovery()

    def test_database_connection_quotes_do_not_turn_password_text_into_a_database(self):
        for text in ['Password="semi;Database=decoy;quote";Database=real_db;',
                     "Password='semi;quote''quote'; Initial Catalog = 'real_db';",
                     'Password="backslash\\";Database=real_db;', ' ;DATABASE=real_db ;']:
            self.assertEqual(connection_database(text), "real_db")
        for text in ["Password=only", "Database=a;Database=b", "Database=a;Initial Catalog=b", "Database=host=foreign",
                     'Password="not closed;Database=foreign', 'Database="real_db"tail', "Database=../secret"]:
            with self.subTest(text=text), self.assertRaises(ValueError): connection_database(text)
