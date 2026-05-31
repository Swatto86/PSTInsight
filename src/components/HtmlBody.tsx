// Renders an untrusted HTML email body safely:
//   1. DOMPurify strips scripts, event handlers, and dangerous tags.
//   2. The result is placed in a fully sandboxed iframe (no scripts, no
//      same-origin) so nothing can reach the app or its IPC bridge.
//   3. A per-frame CSP blocks remote resource loads (tracking pixels) until the
//      user explicitly opts in, like a real mail client.

import { useMemo, useState } from "react";
import DOMPurify from "dompurify";
import { ImageOff, Image as ImageIcon } from "lucide-react";

const REMOTE_RE = /(?:src|background)\s*=\s*["']?\s*https?:|url\(\s*['"]?\s*https?:/i;

function buildSrcDoc(cleanHtml: string, allowRemote: boolean): string {
  const imgSrc = allowRemote ? "data: https: http:" : "data:";
  const fontSrc = allowRemote ? "data: https: http:" : "data:";
  const csp = `default-src 'none'; img-src ${imgSrc}; media-src ${imgSrc}; style-src 'unsafe-inline'; font-src ${fontSrc};`;
  return `<!doctype html><html><head>
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta charset="utf-8">
<style>
  html,body{margin:0}
  body{background:#ffffff;color:#111827;padding:18px 20px;
    font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;
    font-size:14px;line-height:1.5;word-wrap:break-word;overflow-wrap:anywhere}
  img{max-width:100%;height:auto}
  a{color:#2563eb}
  table{max-width:100%}
  blockquote{margin:0 0 0 12px;padding-left:12px;border-left:3px solid #d1d5db;color:#374151}
</style></head><body>${cleanHtml}</body></html>`;
}

export function HtmlBody({ html }: { html: string }) {
  const [allowRemote, setAllowRemote] = useState(false);

  const clean = useMemo(
    () =>
      DOMPurify.sanitize(html, {
        FORBID_TAGS: ["script", "iframe", "object", "embed", "form", "base"],
        FORBID_ATTR: ["srcset"],
        ADD_ATTR: ["target"],
      }),
    [html],
  );

  const hasRemote = useMemo(() => REMOTE_RE.test(clean), [clean]);
  const srcDoc = useMemo(() => buildSrcDoc(clean, allowRemote), [clean, allowRemote]);

  return (
    <div className="flex h-full flex-col">
      {hasRemote && !allowRemote && (
        <div className="flex items-center justify-between gap-3 border-b border-[var(--color-border)] bg-[var(--color-surface-3)] px-4 py-2 text-xs text-[var(--color-muted)]">
          <span className="flex items-center gap-2">
            <ImageOff size={14} /> Remote images blocked to protect your privacy.
          </span>
          <button
            onClick={() => setAllowRemote(true)}
            className="flex items-center gap-1.5 rounded-md bg-[var(--color-accent)] px-2.5 py-1 font-medium text-white hover:bg-[var(--color-accent-soft)]"
          >
            <ImageIcon size={13} /> Load images
          </button>
        </div>
      )}
      <iframe
        title="Message body"
        sandbox=""
        srcDoc={srcDoc}
        className="h-full w-full flex-1 border-0 bg-white"
      />
    </div>
  );
}
