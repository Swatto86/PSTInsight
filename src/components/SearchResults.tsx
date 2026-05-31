// Center pane in search mode: hits across all folders, with a folder badge.

import { VList } from "virtua";
import { Paperclip } from "lucide-react";
import type { SearchHit } from "../lib/api";
import { formatListDate, senderLabel } from "../lib/format";

interface Props {
  hits: SearchHit[];
  selectedNid: number | null;
  onSelect: (hit: SearchHit) => void;
}

export function SearchResults({ hits, selectedNid, onSelect }: Props) {
  if (hits.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--color-faint)]">
        No matches
      </div>
    );
  }
  return (
    <VList className="h-full">
      {hits.map((h) => (
        <div
          key={`${h.folder_nid}-${h.nid}`}
          onClick={() => onSelect(h)}
          className={`cursor-pointer border-b border-[var(--color-border)]/60 px-4 py-2.5 ${
            h.nid === selectedNid
              ? "bg-[var(--color-accent)]/15"
              : "hover:bg-[var(--color-surface-2)]"
          }`}
        >
          <div className="flex items-center gap-2">
            <span
              className={`flex-1 truncate text-sm ${
                h.is_read
                  ? "text-[var(--color-muted)]"
                  : "font-semibold text-[var(--color-text)]"
              }`}
            >
              {senderLabel(h.sender_name, h.sender_email)}
            </span>
            {h.has_attachments && (
              <Paperclip size={13} className="shrink-0 text-[var(--color-faint)]" />
            )}
            <span className="shrink-0 text-[11px] text-[var(--color-faint)] tabular-nums">
              {formatListDate(h.date)}
            </span>
          </div>
          <div className="mt-0.5 truncate text-[13px] text-[var(--color-text)]">
            {h.subject || "(no subject)"}
          </div>
          <div className="mt-0.5 truncate text-[11px] text-[var(--color-accent)]/80">
            {h.folder_name}
          </div>
        </div>
      ))}
    </VList>
  );
}
