// Right pane: full message detail — headers, body, attachments.

import { useState } from "react";
import {
  Download,
  Paperclip,
  Mail,
  ChevronDown,
  Save,
  FileText,
} from "lucide-react";
import type { Attachment, ExportFormat, MessageDetail } from "../lib/api";
import { formatDate, formatSize } from "../lib/format";
import { HtmlBody } from "./HtmlBody";

interface Props {
  detail: MessageDetail | null;
  loading: boolean;
  onExport: (format: ExportFormat) => void;
  onSaveAttachment: (att: Attachment) => void;
}

export function MessagePreview({ detail, loading, onExport, onSaveAttachment }: Props) {
  if (loading) {
    return <Centered>Loading message…</Centered>;
  }
  if (!detail) {
    return (
      <Centered>
        <Mail size={40} className="mb-3 text-[var(--color-border-strong)]" />
        Select a message to read
      </Centered>
    );
  }

  const realAttachments = detail.attachments;

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="border-b border-[var(--color-border)] px-5 py-4">
        <div className="flex items-start gap-3">
          <h2 className="selectable flex-1 text-lg font-semibold leading-snug text-[var(--color-text)]">
            {detail.subject || "(no subject)"}
          </h2>
          <ExportMenu onExport={onExport} />
        </div>
        <div className="selectable mt-3 space-y-1 text-[13px]">
          <Row label="From">
            <span className="text-[var(--color-text)]">{detail.sender_name}</span>
            {detail.sender_email && (
              <span className="ml-1.5 text-[var(--color-faint)]">
                &lt;{detail.sender_email}&gt;
              </span>
            )}
          </Row>
          {detail.display_to && <Row label="To">{detail.display_to}</Row>}
          {detail.display_cc && <Row label="Cc">{detail.display_cc}</Row>}
          {detail.date != null && <Row label="Date">{formatDate(detail.date)}</Row>}
        </div>
      </div>

      {/* Body */}
      <div className="min-h-0 flex-1">
        {detail.has_html ? (
          <HtmlBody html={detail.body_html} />
        ) : (
          <pre className="selectable h-full overflow-auto whitespace-pre-wrap break-words px-5 py-4 font-sans text-[13px] leading-relaxed text-[var(--color-text)]">
            {detail.body_text || "(no message body)"}
          </pre>
        )}
      </div>

      {/* Attachments */}
      {realAttachments.length > 0 && (
        <div className="border-t border-[var(--color-border)] bg-[var(--color-surface-2)] px-5 py-3">
          <div className="mb-2 flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-wide text-[var(--color-faint)]">
            <Paperclip size={12} /> {realAttachments.length} attachment
            {realAttachments.length > 1 ? "s" : ""}
          </div>
          <div className="flex flex-wrap gap-2">
            {realAttachments.map((a) => (
              <AttachmentChip key={a.index} att={a} onSave={() => onSaveAttachment(a)} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function AttachmentChip({ att, onSave }: { att: Attachment; onSave: () => void }) {
  const name = att.is_embedded
    ? `${att.embedded_subject || "Embedded message"}.eml`
    : att.filename || "attachment";
  return (
    <button
      onClick={onSave}
      title={`Save ${name}`}
      className="group flex max-w-xs items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface-3)] px-3 py-2 text-left text-[13px] hover:border-[var(--color-accent)]"
    >
      <FileText size={16} className="shrink-0 text-[var(--color-muted)]" />
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[var(--color-text)]">{name}</span>
        {att.size > 0 && (
          <span className="block text-[11px] text-[var(--color-faint)]">
            {formatSize(att.size)}
          </span>
        )}
      </span>
      <Save
        size={14}
        className="shrink-0 text-[var(--color-faint)] group-hover:text-[var(--color-accent)]"
      />
    </button>
  );
}

function ExportMenu({ onExport }: { onExport: (f: ExportFormat) => void }) {
  const [open, setOpen] = useState(false);
  const choose = (f: ExportFormat) => {
    setOpen(false);
    onExport(f);
  };
  return (
    <div className="relative shrink-0">
      <button
        onClick={() => setOpen((o) => !o)}
        onBlur={() => setTimeout(() => setOpen(false), 120)}
        className="flex items-center gap-1.5 rounded-md border border-[var(--color-border)] bg-[var(--color-surface-3)] px-3 py-1.5 text-[13px] text-[var(--color-text)] hover:border-[var(--color-border-strong)]"
      >
        <Download size={14} /> Export <ChevronDown size={13} />
      </button>
      {open && (
        <div className="absolute right-0 z-10 mt-1 w-32 overflow-hidden rounded-md border border-[var(--color-border-strong)] bg-[var(--color-surface-3)] shadow-xl">
          {(["eml", "msg", "txt"] as ExportFormat[]).map((f) => (
            <button
              key={f}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => choose(f)}
              className="block w-full px-3 py-2 text-left text-[13px] text-[var(--color-muted)] hover:bg-[var(--color-accent)]/20 hover:text-[var(--color-text)]"
            >
              .{f}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex">
      <span className="w-12 shrink-0 text-[var(--color-faint)]">{label}</span>
      <span className="min-w-0 flex-1 break-words text-[var(--color-muted)]">{children}</span>
    </div>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex h-full flex-col items-center justify-center text-sm text-[var(--color-faint)]">
      {children}
    </div>
  );
}
