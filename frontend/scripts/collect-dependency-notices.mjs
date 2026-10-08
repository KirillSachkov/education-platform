// Preserve installed npm license files and metadata alongside the standalone app.
import { cpSync, existsSync, mkdirSync, readdirSync, lstatSync, readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join, relative, dirname } from "node:path";

const root = "node_modules";
const output = "dependency-notices/npm";
const nativeNotices = "third-party/sharp-libvips-1.3.4";

// The npm native package omits full GNU texts and embedded component copyrights.
// Verify the reviewed supplement before copying anything to the image output.
const manifest = JSON.parse(readFileSync(join(nativeNotices, "manifest.json"), "utf8"));
for (const required of ["GNU/GPL-3.0.txt", "GNU/LGPL-3.0.txt", "README.md", "versions.json"]) {
  if (!manifest.files[required])
    throw new Error(`Missing native notice manifest entry: ${required}`);
}
for (const [path, expected] of Object.entries(manifest.files)) {
  if (path.startsWith("/") || path.split("/").includes("..")) {
    throw new Error(`Invalid native notice path: ${path}`);
  }
  const file = join(nativeNotices, path);
  if (!lstatSync(file).isFile() || lstatSync(file).isSymbolicLink()) {
    throw new Error(`Native notice must be an ordinary file: ${path}`);
  }
  if (createHash("sha256").update(readFileSync(file)).digest("hex") !== expected) {
    throw new Error(`Native notice checksum mismatch: ${path}`);
  }
}

const sharp = JSON.parse(readFileSync(join(root, "sharp/package.json"), "utf8"));
if (sharp.version !== manifest.sharp_version) throw new Error("Sharp notice version mismatch");
const nativePackage = join(root, manifest.native_package);
const isMuslX64 =
  process.platform === "linux" &&
  process.arch === "x64" &&
  !process.report.getReport().header.glibcVersionRuntime;
if (isMuslX64 && !existsSync(nativePackage))
  throw new Error("Missing Linux musl x64 libvips package");
if (existsSync(nativePackage)) {
  const metadata = JSON.parse(readFileSync(join(nativePackage, "package.json"), "utf8"));
  if (metadata.version !== manifest.native_version)
    throw new Error("libvips notice version mismatch");
  const installed = JSON.parse(readFileSync(join(nativePackage, "versions.json"), "utf8"));
  const expected = JSON.parse(readFileSync(join(nativeNotices, "versions.json"), "utf8"));
  if (
    Object.keys(installed).length !== Object.keys(expected).length ||
    Object.entries(expected).some(([name, version]) => installed[name] !== version)
  ) {
    throw new Error("libvips embedded component versions mismatch");
  }
}

function collect(directory) {
  for (const entry of readdirSync(directory)) {
    const source = join(directory, entry);
    if (lstatSync(source).isSymbolicLink()) continue;
    if (lstatSync(source).isDirectory()) {
      collect(source);
    } else if (
      /^(license|licence|notice|copying|copyright|authors)([.-]|$)/i.test(entry) ||
      entry === "package.json" ||
      entry === "versions.json" ||
      /^readme([.-]|$)/i.test(entry)
    ) {
      const target = join(output, relative(root, source));
      mkdirSync(dirname(target), { recursive: true });
      cpSync(source, target);
    }
  }
}

if (!existsSync(root)) throw new Error("Install dependencies before collecting notices");
collect(root);
cpSync(nativeNotices, "dependency-notices/native-libvips", { recursive: true });
