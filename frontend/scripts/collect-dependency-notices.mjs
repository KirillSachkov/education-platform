// Preserve installed npm license files and metadata alongside the standalone app.
import { cpSync, existsSync, mkdirSync, readdirSync, lstatSync } from "node:fs";
import { join, relative, dirname } from "node:path";

const root = "node_modules";
const output = "dependency-notices/npm";

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
