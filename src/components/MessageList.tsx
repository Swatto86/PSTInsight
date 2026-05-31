// Center pane: a virtualized list of message summaries.

import { VList } from "virtua";
import { Paperclip } from "lucide-react";
import type { MessageSummary } from "../lib/api";
import { formatListDate, formatSize, senderLabel } from "../lib/format";

interface Props {
  messages: MessageSummary[];
  selectedNid: number | null;
  onSelect: (m: MessageSummary) => void;
}

export function MessageList({ messages, selectedNid, onSelect }: Props) {
  if (messages.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--color-faint)]">
        No messages in this folder
      </div>
    );
  }

  return (
    <VList className="h-full">
      {messages.map((m) => (
        <MessageRow
          key={m.nid}
          m={m}
          selected={m.nid === selectedNid}
          onSelect={onSelect}
        />
      ))}
    </VList>
  );
}

function MessageRow({
  m,
  selected,
  onSelect,
}: {
  m: MessageSummary;
  selected: boolean;
  onSelect: (m: MessageSummary) => void;
}) {
  return (
    <div
      onClick={() => onSelect(m)}
      className={`cursor-pointer border-b border-[var(--color-border)]/60 px-4 py-2.5 ${
        selected
          ? "bg-[var(--color-accent)]/15"
          : "hover:bg-[var(--color-surface-2)]"
      }`}
    >
      <div className="flex items-center gap-2">
        <span
          className={`h-2 w-2 shrink-0 rounded-full ${
            m.is_read ? "bg-transparent" : "bg-[var(--color-unread)]"
          }`}
        />
        <span
          className={`flex-1 truncate text-sm ${
            m.is_read ? "text-[var(--color-muted)]" : "font-semibold text-[var(--color-text)]"
          }`}
        >
          {senderLabel(m.sender_name, m.sender_email)}
        </span>
        {m.has_attachments && (
          <Paperclip size={13} className="shrink-0 text-[var(--color-faint)]" />
        )}
        <span className="shrink-0 text-[11px] text-[var(--color-faint)] tabular-nums">
          {formatListDate(m.date)}
        </span>
      </div>
      <div className="mt-0.5 flex items-center gap-2 pl-4">
        <span
          className={`flex-1 truncate text-[13px] ${
            m.is_read ? "text-[var(--color-faint)]" : "text-[var(--color-text)]"
          }`}
        >
          {m.subject || "(no subject)"}
        </span>
        {m.size > 0 && (
          <span className="shrink-0 text-[11px] text-[var(--color-faint)] tabular-nums">
            {formatSize(m.size)}
          </span>
        )}
      </div>
    </div>
  );
}
