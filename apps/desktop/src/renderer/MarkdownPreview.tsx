import { createElement, Fragment, type ReactNode } from "react";

const inlineTokenPattern = /(`[^`\n]+`|\*\*[^*\n]+\*\*|~~[^~\n]+~~|\[[^\]\n]+\]\([^\s\n]+\)|\*[^*\n]+\*|\n)/g;

export function MarkdownPreview({ content }: { readonly content: string }) {
  const lines = content.replace(/\r\n?/g, "\n").split("\n");
  const blocks: ReactNode[] = [];
  let index = 0;

  while (index < lines.length) {
    const line = lines[index]!;
    if (line.trim().length === 0) {
      index++;
      continue;
    }

    const fence = line.match(/^\s{0,3}```\s*([\w+-]*)\s*$/);
    if (fence) {
      const code: string[] = [];
      index++;
      while (index < lines.length && !/^\s{0,3}```\s*$/.test(lines[index]!)) code.push(lines[index++]!);
      if (index < lines.length) index++;
      const language = fence[1] ? `language-${fence[1]}` : undefined;
      blocks.push(<pre key={`code-${blocks.length}`}><code className={language}>{code.join("\n")}</code></pre>);
      continue;
    }

    const heading = line.match(/^(#{1,6})\s+(.+)$/);
    if (heading) {
      const level = heading[1]!.length;
      blocks.push(createElement(`h${level}`, { key: `heading-${blocks.length}` }, inline(heading[2]!)));
      index++;
      continue;
    }

    if (/^\s{0,3}(?:[-*_]\s*){3,}$/.test(line)) {
      blocks.push(<hr key={`rule-${blocks.length}`} />);
      index++;
      continue;
    }

    const listStart = line.match(/^\s{0,3}([-+*]|\d+\.)\s+(.+)$/);
    if (listStart) {
      const ordered = /\d+\./.test(listStart[1]!);
      const items: ReactNode[] = [];
      while (index < lines.length) {
        const item = lines[index]!.match(/^\s{0,3}([-+*]|\d+\.)\s+(.+)$/);
        if (!item || /\d+\./.test(item[1]!) !== ordered) break;
        items.push(<li key={`item-${items.length}`}>{inline(item[2]!)}</li>);
        index++;
      }
      blocks.push(createElement(ordered ? "ol" : "ul", { key: `list-${blocks.length}` }, items));
      continue;
    }

    if (/^\s{0,3}>\s?/.test(line)) {
      const quote: string[] = [];
      while (index < lines.length && /^\s{0,3}>\s?/.test(lines[index]!)) {
        quote.push(lines[index++]!.replace(/^\s{0,3}>\s?/, ""));
      }
      blocks.push(<blockquote key={`quote-${blocks.length}`}>{inline(quote.join("\n"))}</blockquote>);
      continue;
    }

    const paragraph = [line];
    index++;
    while (index < lines.length && lines[index]!.trim().length > 0 && !startsBlock(lines[index]!)) {
      paragraph.push(lines[index++]!);
    }
    blocks.push(<p key={`paragraph-${blocks.length}`}>{inline(paragraph.join("\n"))}</p>);
  }

  return <div className="markdown-preview">{blocks}</div>;
}

function startsBlock(line: string): boolean {
  return /^\s{0,3}```/.test(line) || /^(#{1,6})\s+/.test(line) ||
    /^\s{0,3}(?:[-*_]\s*){3,}$/.test(line) || /^\s{0,3}([-+*]|\d+\.)\s+/.test(line) ||
    /^\s{0,3}>\s?/.test(line);
}

function inline(value: string): ReactNode[] {
  const output: ReactNode[] = [];
  let cursor = 0;
  let match: RegExpExecArray | null;
  const tokenPattern = new RegExp(inlineTokenPattern);
  while ((match = tokenPattern.exec(value)) !== null) {
    if (match.index > cursor) output.push(value.slice(cursor, match.index));
    const token = match[0]!;
    const key = `inline-${output.length}`;
    if (token === "\n") output.push(<br key={key} />);
    else if (token.startsWith("`")) output.push(<code key={key}>{token.slice(1, -1)}</code>);
    else if (token.startsWith("**")) output.push(<strong key={key}>{inline(token.slice(2, -2))}</strong>);
    else if (token.startsWith("~~")) output.push(<del key={key}>{inline(token.slice(2, -2))}</del>);
    else if (token.startsWith("*")) output.push(<em key={key}>{inline(token.slice(1, -1))}</em>);
    else {
      const divider = token.indexOf("](");
      const label = token.slice(1, divider);
      const href = token.slice(divider + 2, -1);
      output.push(isSafeLink(href)
        ? <a key={key} href={href} target="_blank" rel="noreferrer noopener">{inline(label)}</a>
        : <Fragment key={key}>{label}</Fragment>);
    }
    cursor = match.index + token.length;
  }
  if (cursor < value.length) output.push(value.slice(cursor));
  return output;
}

function isSafeLink(href: string): boolean {
  try {
    const protocol = new URL(href).protocol.toLowerCase();
    return protocol === "https:" || protocol === "http:" || protocol === "mailto:";
  } catch {
    return false;
  }
}
