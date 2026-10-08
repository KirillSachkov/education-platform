import { cn } from "@/shared/lib/css";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";

/** В тексте есть код — inline `…` или ```fenced```-блок. Тогда рендерим markdown. */
function hasCode(text: string): boolean {
  return text.includes("`");
}

/**
 * Текст вопроса / пояснения теста (#566): markdown-рендер только когда внутри есть
 * код (например ```csharp```-блок «что выведет код») — иначе обычный span, чтобы
 * prose-маргины не раздували строку. Покрывает и inline `code`, и fenced-блоки.
 */
export function QuizRichText({ text, className }: { text: string; className?: string }) {
  if (!hasCode(text)) {
    return <span className={className}>{text}</span>;
  }
  return (
    <div
      className={cn(
        "min-w-0 [&_p]:my-0 [&_p]:text-sm [&_p]:leading-snug [&_pre]:my-2 [&_pre]:max-w-full [&_pre]:overflow-x-auto [&_pre_code]:!whitespace-pre",
        className,
      )}
    >
      <MarkdownContent variant="compact" disableLinks>
        {text}
      </MarkdownContent>
    </div>
  );
}

/**
 * Текст варианта ответа (#528): markdown-рендер только когда внутри есть код
 * (вариант-сниппет для заданий «выбери правильный пример кода») — иначе
 * обычный span, чтобы prose-маргины не раздували строку варианта.
 */
export function QuizOptionContent({ text }: { text: string }) {
  if (!hasCode(text)) {
    return <span className="text-sm leading-snug">{text}</span>;
  }
  return (
    <div className="min-w-0 flex-1 text-sm leading-snug [&_p]:my-0 [&_p]:text-sm [&_p]:leading-snug [&_pre]:my-1.5 [&_pre]:max-w-full [&_pre]:overflow-x-auto [&_pre_code]:!whitespace-pre">
      <MarkdownContent variant="compact" disableLinks>
        {text}
      </MarkdownContent>
    </div>
  );
}
