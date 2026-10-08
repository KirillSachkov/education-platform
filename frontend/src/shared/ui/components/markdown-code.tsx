"use client";

import { Check, Copy } from "lucide-react";
import {
  type CSSProperties,
  type ComponentProps,
  type ReactNode,
  useEffect,
  useRef,
  useState,
} from "react";
import { PrismLight as SyntaxHighlighter } from "react-syntax-highlighter";
import bash from "react-syntax-highlighter/dist/esm/languages/prism/bash";
import csharp from "react-syntax-highlighter/dist/esm/languages/prism/csharp";
import javascript from "react-syntax-highlighter/dist/esm/languages/prism/javascript";
import json from "react-syntax-highlighter/dist/esm/languages/prism/json";
import jsx from "react-syntax-highlighter/dist/esm/languages/prism/jsx";
import markdown from "react-syntax-highlighter/dist/esm/languages/prism/markdown";
import markup from "react-syntax-highlighter/dist/esm/languages/prism/markup";
import tsx from "react-syntax-highlighter/dist/esm/languages/prism/tsx";
import typescript from "react-syntax-highlighter/dist/esm/languages/prism/typescript";
import yaml from "react-syntax-highlighter/dist/esm/languages/prism/yaml";

const MONO_FONT_STACK =
  'var(--font-jetbrains-mono), "JetBrains Mono", ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace';

const LANGUAGE_ALIASES: Record<string, string> = {
  js: "javascript",
  jsx: "jsx",
  ts: "typescript",
  tsx: "tsx",
  cs: "csharp",
  csharp: "csharp",
  sh: "bash",
  shell: "bash",
  yml: "yaml",
  md: "markdown",
};

SyntaxHighlighter.registerLanguage("bash", bash);
SyntaxHighlighter.registerLanguage("csharp", csharp);
SyntaxHighlighter.registerLanguage("javascript", javascript);
SyntaxHighlighter.registerLanguage("json", json);
SyntaxHighlighter.registerLanguage("jsx", jsx);
SyntaxHighlighter.registerLanguage("markdown", markdown);
SyntaxHighlighter.registerLanguage("markup", markup);
SyntaxHighlighter.registerLanguage("tsx", tsx);
SyntaxHighlighter.registerLanguage("typescript", typescript);
SyntaxHighlighter.registerLanguage("yaml", yaml);

const syntaxTheme: Record<string, CSSProperties> = {
  'code[class*="language-"]': {
    color: "var(--code-block-text)",
    background: "transparent",
    fontFamily: MONO_FONT_STACK,
    fontSize: "0.875rem",
    lineHeight: "1.65",
    textShadow: "none",
  },
  'pre[class*="language-"]': {
    color: "var(--code-block-text)",
    background: "transparent",
    fontFamily: MONO_FONT_STACK,
    fontSize: "0.875rem",
    lineHeight: "1.65",
    textShadow: "none",
  },
  comment: {
    color: "var(--syntax-comment)",
    fontStyle: "italic",
  },
  prolog: {
    color: "var(--syntax-comment)",
  },
  doctype: {
    color: "var(--syntax-comment)",
  },
  cdata: {
    color: "var(--syntax-comment)",
  },
  punctuation: {
    color: "var(--syntax-punctuation)",
  },
  keyword: {
    color: "var(--syntax-keyword)",
    fontWeight: "600",
  },
  "keyword.control-flow": {
    color: "var(--syntax-keyword)",
    fontWeight: "600",
  },
  atrule: {
    color: "var(--syntax-keyword)",
    fontWeight: "600",
  },
  tag: {
    color: "var(--syntax-keyword)",
  },
  string: {
    color: "var(--syntax-string)",
  },
  char: {
    color: "var(--syntax-string)",
  },
  "attr-value": {
    color: "var(--syntax-string)",
  },
  inserted: {
    color: "var(--syntax-string)",
  },
  number: {
    color: "var(--syntax-number)",
  },
  boolean: {
    color: "var(--syntax-number)",
  },
  constant: {
    color: "var(--syntax-number)",
  },
  regex: {
    color: "var(--syntax-number)",
  },
  function: {
    color: "var(--syntax-function)",
  },
  entity: {
    color: "var(--syntax-function)",
  },
  url: {
    color: "var(--syntax-function)",
  },
  "class-name": {
    color: "var(--syntax-class)",
  },
  selector: {
    color: "var(--syntax-class)",
  },
  builtin: {
    color: "var(--syntax-class)",
  },
  symbol: {
    color: "var(--syntax-class)",
  },
  property: {
    color: "var(--syntax-property)",
  },
  "attr-name": {
    color: "var(--syntax-property)",
  },
  variable: {
    color: "var(--syntax-property)",
  },
  operator: {
    color: "var(--syntax-operator)",
  },
  important: {
    color: "var(--syntax-operator)",
    fontWeight: "700",
  },
  deleted: {
    color: "var(--red)",
  },
};

interface MarkdownCodeProps extends ComponentProps<"code"> {
  allowCopy?: boolean;
}

function extractCodeText(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") {
    return String(node);
  }

  if (Array.isArray(node)) {
    return node.map(extractCodeText).join("");
  }

  return "";
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  const handleCopy = () => {
    navigator.clipboard.writeText(text).catch(() => {});
    clearTimeout(timeoutRef.current);
    setCopied(true);
    timeoutRef.current = setTimeout(() => setCopied(false), 2000);
  };

  useEffect(() => {
    return () => clearTimeout(timeoutRef.current);
  }, []);

  return (
    <button
      type="button"
      onClick={handleCopy}
      className="inline-flex size-7 items-center justify-center rounded-md bg-background/80 text-muted-foreground transition-colors hover:bg-background hover:text-foreground"
      aria-label={copied ? "Код скопирован" : "Скопировать код"}
      title={copied ? "Код скопирован" : "Скопировать код"}
    >
      {copied ? <Check size={12} className="text-emerald-600" /> : <Copy size={12} />}
    </button>
  );
}

export function MarkdownCode({
  children,
  className,
  allowCopy = false,
}: MarkdownCodeProps) {
  const rawCode = extractCodeText(children).replace(/\n$/, "");
  const languageToken = className?.match(/language-([\w-]+)/)?.[1]?.toLowerCase();
  const language = languageToken ? (LANGUAGE_ALIASES[languageToken] ?? languageToken) : undefined;
  const isInline = !language && !rawCode.includes("\n");

  if (isInline) {
    return (
      <code
        className="rounded px-1.5 py-0.5 font-mono text-sm"
        style={{
          backgroundColor: "var(--code-bg)",
          border: "1px solid var(--code-border)",
          color: "var(--code-text)",
          fontFamily: MONO_FONT_STACK,
        }}
      >
        {children}
      </code>
    );
  }

  return (
    <div
      className="markdown-code-block mb-4 overflow-hidden"
      style={{
        backgroundColor: "var(--code-bg)",
        border: "0",
        borderRadius: "var(--radius-xl)",
        boxShadow: "none",
      }}
    >
      <div
        className="flex items-center justify-between border-b px-3 py-2"
        style={{
          backgroundColor: "transparent",
          borderColor: "var(--code-border)",
        }}
      >
        <span
          className="text-[11px] font-semibold uppercase tracking-[0.16em]"
          style={{
            color: "var(--code-header-text)",
            fontFamily: MONO_FONT_STACK,
          }}
        >
          {language ?? "code"}
        </span>
        {allowCopy ? <CopyButton text={rawCode} /> : null}
      </div>

      <SyntaxHighlighter
        language={language}
        style={syntaxTheme}
        customStyle={{
          margin: 0,
          background: "transparent",
          border: "0",
          borderRadius: "0",
          padding: "1rem",
          overflowX: "auto",
        }}
        codeTagProps={{
          style: {
            color: "var(--code-block-text)",
            fontFamily: MONO_FONT_STACK,
          },
        }}
      >
        {rawCode}
      </SyntaxHighlighter>
    </div>
  );
}
