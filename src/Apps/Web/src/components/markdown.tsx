import * as React from "react"
import { cn } from "cn"

// What agents write, drawn as text: paragraphs, headings, lists, quotes, rules, code blocks, inline
// code, bold, italics, and links. It builds React elements from the text and never HTML, so nothing
// an agent writes can run in the page. Links open only http, https, and mailto addresses.
// Not handled: tables and nested lists deeper than their indentation shows; they read as text.

type Block =
  | { type: "paragraph"; text: string }
  | { type: "heading"; level: number; text: string }
  | { type: "code"; language: string; text: string }
  | {
      type: "list"
      ordered: boolean
      start: number
      items: { indent: number; text: string }[]
    }
  | { type: "quote"; text: string }
  | { type: "rule" }

const listItem = /^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$/

function blocks(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, "\n").split("\n")
  const found: Block[] = []
  let index = 0
  while (index < lines.length) {
    const line = lines[index]
    const fence = /^\s*(```|~~~)\s*([\w+-]*)/.exec(line)
    if (fence) {
      const code: string[] = []
      index++
      while (
        index < lines.length &&
        !lines[index].trimStart().startsWith(fence[1])
      ) {
        code.push(lines[index++])
      }
      index++
      found.push({ type: "code", language: fence[2], text: code.join("\n") })
      continue
    }
    if (line.trim() === "") {
      index++
      continue
    }
    const heading = /^(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line)
    if (heading) {
      found.push({
        type: "heading",
        level: heading[1].length,
        text: heading[2],
      })
      index++
      continue
    }
    if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) {
      found.push({ type: "rule" })
      index++
      continue
    }
    if (/^\s*>/.test(line)) {
      const quote: string[] = []
      while (index < lines.length && /^\s*>/.test(lines[index])) {
        quote.push(lines[index++].replace(/^\s*>\s?/, ""))
      }
      found.push({ type: "quote", text: quote.join("\n") })
      continue
    }
    const first = listItem.exec(line)
    if (first) {
      const ordered = /\d/.test(first[2])
      const items: { indent: number; text: string }[] = []
      while (index < lines.length) {
        const item = listItem.exec(lines[index])
        if (item) {
          items.push({ indent: item[1].length, text: item[3] })
        } else if (/^\s+\S/.test(lines[index]) && items.length > 0) {
          items[items.length - 1].text += " " + lines[index].trim()
        } else {
          break
        }
        index++
      }
      found.push({
        type: "list",
        ordered,
        start: ordered ? parseInt(first[2], 10) : 1,
        items,
      })
      continue
    }
    const paragraph: string[] = []
    while (
      index < lines.length &&
      lines[index].trim() !== "" &&
      !/^\s*(```|~~~|#{1,6}\s|>)/.test(lines[index]) &&
      !(paragraph.length > 0 && listItem.test(lines[index]))
    ) {
      paragraph.push(lines[index++])
    }
    found.push({ type: "paragraph", text: paragraph.join("\n") })
  }
  return found
}

const inlinePattern =
  /(`+)([\s\S]*?[^`])\1(?!`)|\*\*([\s\S]+?)\*\*|__([\s\S]+?)__|\*([^\s*][\s\S]*?)\*|(?<![\w])_([^\s_][\s\S]*?)_(?![\w])|\[([^\]]+)\]\(([^)\s]+)\)|(https?:\/\/[^\s<>()]+[^\s<>().,;:!?'"])/g

function inline(text: string, key = ""): React.ReactNode[] {
  const nodes: React.ReactNode[] = []
  let last = 0
  for (const match of text.matchAll(inlinePattern)) {
    const at = match.index
    if (at > last) {
      nodes.push(text.slice(last, at))
    }
    const k = `${key}${at}`
    if (match[1]) {
      nodes.push(<code key={k}>{match[2]}</code>)
    } else if (match[3] ?? match[4]) {
      nodes.push(<strong key={k}>{inline(match[3] ?? match[4], k)}</strong>)
    } else if (match[5] ?? match[6]) {
      nodes.push(<em key={k}>{inline(match[5] ?? match[6], k)}</em>)
    } else if (match[7]) {
      nodes.push(link(match[8], inline(match[7], k), k))
    } else if (match[9]) {
      nodes.push(link(match[9], match[9], k))
    }
    last = at + match[0].length
  }
  if (last < text.length) {
    nodes.push(text.slice(last))
  }
  return nodes
}

function link(
  href: string,
  children: React.ReactNode,
  key: string
): React.ReactNode {
  const safe = /^(https?:|mailto:)/i.test(href)
  return safe ? (
    <a
      key={key}
      href={href}
      target="_blank"
      rel="noreferrer noopener"
      className="font-medium text-primary underline decoration-primary/40 underline-offset-2 hover:decoration-primary"
    >
      {children}
    </a>
  ) : (
    <React.Fragment key={key}>{children}</React.Fragment>
  )
}

export function Markdown({
  text,
  className,
}: {
  text: string
  className?: string
}) {
  return (
    <div className={cn("flex flex-col gap-3", className)}>
      {blocks(text).map((block, index) => {
        switch (block.type) {
          case "paragraph":
            return (
              <p key={index} className="text-pretty whitespace-pre-line">
                {inline(block.text)}
              </p>
            )
          case "heading":
            return (
              <p
                key={index}
                role="heading"
                aria-level={block.level}
                className="font-semibold"
              >
                {inline(block.text)}
              </p>
            )
          case "code":
            return (
              <pre
                key={index}
                className="overflow-x-auto rounded-lg bg-muted px-3.5 py-3 text-xs leading-relaxed"
              >
                <code className="bg-transparent p-0 text-[length:inherit]">
                  {block.text}
                </code>
              </pre>
            )
          case "list": {
            const List = block.ordered ? "ol" : "ul"
            return (
              <List
                key={index}
                start={block.ordered ? block.start : undefined}
                className={cn(
                  "flex flex-col gap-1 pl-5",
                  block.ordered ? "list-decimal" : "list-disc"
                )}
              >
                {block.items.map((item, itemIndex) => (
                  <li
                    key={itemIndex}
                    className="pl-1 marker:text-muted-foreground"
                    style={
                      item.indent > 1
                        ? { marginLeft: `${Math.min(item.indent, 8) * 0.5}rem` }
                        : undefined
                    }
                  >
                    {inline(item.text)}
                  </li>
                ))}
              </List>
            )
          }
          case "quote":
            return (
              <blockquote
                key={index}
                className="border-l-2 border-border pl-3 text-muted-foreground"
              >
                <Markdown text={block.text} />
              </blockquote>
            )
          case "rule":
            return <hr key={index} className="border-border" />
        }
      })}
    </div>
  )
}
