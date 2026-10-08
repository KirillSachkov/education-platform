import assert from "node:assert/strict";
import { cpSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { test } from "node:test";

const frontend = join(dirname(fileURLToPath(import.meta.url)), "..");
const bundle = join(frontend, "third-party/sharp-libvips-1.3.4");
const script = join(frontend, "scripts/collect-dependency-notices.mjs");
const manifest = JSON.parse(readFileSync(join(bundle, "manifest.json"), "utf8"));

function fixture(t) {
  const cwd = mkdtempSync(join(tmpdir(), "native-notices-"));
  t.after(() => rmSync(cwd, { recursive: true, force: true }));
  const notices = join(cwd, "third-party/sharp-libvips-1.3.4");
  cpSync(bundle, notices, { recursive: true });
  const native = join(cwd, "node_modules", manifest.native_package);
  mkdirSync(native, { recursive: true });
  mkdirSync(join(cwd, "node_modules/sharp"));
  writeFileSync(join(cwd, "node_modules/sharp/package.json"), '{"version":"0.35.5"}');
  writeFileSync(join(native, "package.json"), '{"version":"1.3.4"}');
  cpSync(join(notices, "versions.json"), join(native, "versions.json"));
  return {
    cwd,
    notices,
    native,
    run: () => spawnSync(process.execPath, [script], { cwd, encoding: "utf8" }),
  };
}

test("collects full GNU texts and original component copyrights without changing bytes", (t) => {
  const f = fixture(t);
  const result = f.run();
  assert.equal(result.status, 0, result.stderr);
  for (const path of Object.keys(manifest.files)) {
    assert.deepEqual(
      readFileSync(join(f.cwd, "dependency-notices/native-libvips", path)),
      readFileSync(join(bundle, path)),
    );
  }
});

test("rejects a missing complete GNU license", (t) => {
  const f = fixture(t);
  rmSync(join(f.notices, "GNU/LGPL-3.0.txt"));
  assert.notEqual(f.run().status, 0);
});

test("rejects changed embedded component copyright text", (t) => {
  const f = fixture(t);
  const path = "native/29-v0.5.4/LICENSE";
  assert.match(readFileSync(join(f.notices, path), "utf8"), /Daniel Löbl/);
  writeFileSync(join(f.notices, path), "MIT\n");
  const result = f.run();
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Native notice checksum mismatch/);
});

test("rejects a native package upgrade without matching source and notices", (t) => {
  const f = fixture(t);
  writeFileSync(join(f.native, "package.json"), '{"version":"1.3.5"}');
  const result = f.run();
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /libvips notice version mismatch/);
});

test("rejects changed embedded component versions", (t) => {
  const f = fixture(t);
  const versions = JSON.parse(readFileSync(join(f.native, "versions.json"), "utf8"));
  versions.cgif = "0.5.5";
  writeFileSync(join(f.native, "versions.json"), JSON.stringify(versions));
  const result = f.run();
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /libvips embedded component versions mismatch/);
});
