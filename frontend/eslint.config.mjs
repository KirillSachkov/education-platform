import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import tanstackQueryPlugin from "@tanstack/eslint-plugin-query";
import boundaries from "eslint-plugin-boundaries";
import tseslint from "typescript-eslint";

// #712 — правила strictTypeChecked с существующим долгом (замер 2026-07-06).
// Держатся на warn + per-rule ratchet в CI (scripts/ci/eslint-ratchet.sh,
// baseline frontend/.eslint-warnings-baseline.json): число нарушений может
// только падать. Выжег правило до нуля — убери его отсюда, оно станет error.
const ESLINT_RATCHET_RULES = [
  "@typescript-eslint/no-confusing-void-expression",
  "@typescript-eslint/restrict-template-expressions",
  "@typescript-eslint/no-unnecessary-condition",
  "@typescript-eslint/no-non-null-assertion",
  "@typescript-eslint/no-misused-promises",
  "@typescript-eslint/no-floating-promises",
  "@typescript-eslint/no-deprecated",
  "@typescript-eslint/no-unsafe-member-access",
  "@typescript-eslint/no-unsafe-assignment",
  "@typescript-eslint/no-unnecessary-type-assertion",
  "@typescript-eslint/no-unnecessary-type-arguments",
  "@typescript-eslint/require-await",
  "@typescript-eslint/no-unsafe-call",
  "@typescript-eslint/no-unsafe-argument",
  "@typescript-eslint/no-invalid-void-type",
  "@typescript-eslint/no-redundant-type-constituents",
  "@typescript-eslint/use-unknown-in-catch-callback-variable",
  "@typescript-eslint/no-unsafe-return",
  "@typescript-eslint/unbound-method",
  "@typescript-eslint/no-unnecessary-type-conversion",
  "@typescript-eslint/no-unnecessary-boolean-literal-compare",
  "@typescript-eslint/return-await",
  "@typescript-eslint/prefer-promise-reject-errors",
  "@typescript-eslint/no-unnecessary-type-parameters",
  "@typescript-eslint/no-misused-spread",
  "@typescript-eslint/no-base-to-string",
  "@typescript-eslint/restrict-plus-operands",
  "@typescript-eslint/no-unnecessary-template-expression",
];

// Typed-linting (strictTypeChecked) требует полный type-graph — медленно,
// поэтому включается только с LINT_TYPED=1 (npm run lint, CI eslint-ratchet).
// Быстрый путь без него: PostToolUse-хук, lint-staged, редактор.
const typedStrict =
  process.env.LINT_TYPED === "1"
    ? [
        ...tseslint.configs.strictTypeChecked.map((c) => ({
          ...c,
          files: ["src/**/*.{ts,tsx}"],
        })),
        {
          files: ["src/**/*.{ts,tsx}"],
          languageOptions: {
            parserOptions: {
              projectService: true,
              tsconfigRootDir: import.meta.dirname,
            },
          },
        },
        {
          files: ["src/**/*.{ts,tsx}"],
          rules: Object.fromEntries(
            ESLINT_RATCHET_RULES.map((rule) => [rule, "warn"]),
          ),
        },
      ]
    : [];

// Feature-Sliced Design layers, top → bottom. Each layer may import from
// itself or any layer below. See docs/agents/frontend-fsd.md.
//
// Features layer has slice capture — cross-feature imports are forbidden
// (lift shared logic to entities or shared). Widgets and entities allow
// cross-slice imports as a pragmatic exception: widgets legitimately compose
// sibling widgets (layouts use sidebar), and entities often re-export tightly
// coupled DTOs from peer slices (ECS domain — course/material/issue/module/
// project — has interlocking type references).
const fsdLayers = [
  { type: "app", pattern: "src/app/**" },
  { type: "pages", pattern: "src/pages/**" },
  { type: "widgets", pattern: "src/widgets/**" },
  { type: "features", pattern: "src/features/*", mode: "folder", capture: ["slice"] },
  { type: "entities", pattern: "src/entities/**" },
  { type: "shared", pattern: "src/shared/**" },
];

// Same-slice helper for the features layer.
const sameSlice = (type) => ({
  type,
  captured: { slice: "{{from.captured.slice}}" },
});

const fsdRules = [
  {
    from: { type: "app" },
    allow: { to: { type: ["pages", "widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "pages" },
    allow: { to: { type: ["widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "widgets" },
    allow: { to: { type: ["widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "features" },
    allow: { to: [sameSlice("features"), { type: "entities" }, { type: "shared" }] },
  },
  {
    from: { type: "entities" },
    allow: { to: { type: ["entities", "shared"] } },
  },
  { from: { type: "shared" }, allow: { to: { type: ["shared"] } } },
];

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  ...tanstackQueryPlugin.configs["flat/recommended"],
  ...typedStrict,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    // Legacy Vite prototype — separate project with own config:
    "project/**",
  ]),
  {
    plugins: { boundaries },
    settings: {
      "boundaries/elements": fsdLayers,
      "boundaries/include": ["src/**/*"],
      // Resolve `@/` alias so boundaries can match imports against `fsdLayers`
      // patterns. Without this every `@/features/...` import is treated as
      // external and the FSD check silently passes.
      "import/resolver": {
        typescript: {
          alwaysTryTypes: true,
          project: "./tsconfig.json",
        },
      },
    },
    rules: {
      "boundaries/dependencies": [
        "error",
        { default: "disallow", rules: fsdRules },
      ],
    },
  },
  {
    rules: {
      // react-hook-form's `watch()` is intentionally non-memoizable; React
      // Compiler silently skips the component, which is fine for our usage.
      // The rule is informational only — silence it to keep `npm run lint` clean.
      "react-hooks/incompatible-library": "off",

      // Force `import type { ... }` for type-only imports so unused imports
      // get elided by the bundler instead of tree-shaken at runtime.
      "@typescript-eslint/consistent-type-imports": [
        "error",
        { prefer: "type-imports", fixStyle: "inline-type-imports" },
      ],

      // Unused vars — allow `_`-prefix convention for intentional placeholders.
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
          ignoreRestSiblings: true,
        },
      ],

      // no-console — deliberate logs must go through `logger` / `toast`.
      "no-console": ["error", { allow: ["warn", "error"] }],
    },
  },
]);

export default eslintConfig;
