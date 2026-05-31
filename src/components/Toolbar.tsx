// Top bar: open file, current store, and search.

import { FolderOpen, Search, X, FileSearch } from "lucide-react";

interface Props {
  fileName: string | null;
  format: string | null;
  query: string;
  deep: boolean;
  searching: boolean;
  onOpen: () => void;
  onQueryChange: (q: string) => void;
  onToggleDeep: () => void;
  onClearSearch: () => void;
}

export function Toolbar({
  fileName,
  format,
  query,
  deep,
  searching,
  onOpen,
  onQueryChange,
  onToggleDeep,
  onClearSearch,
}: Props) {
  return (
    <div className="flex h-12 shrink-0 items-center gap-3 border-b border-[var(--color-border)] bg-[var(--color-surface)] px-3">
      <div className="flex items-center gap-2 pr-1">
        <FileSearch size={18} className="text-[var(--color-accent)]" />
        <span className="text-sm font-semibold tracking-tight text-[var(--color-text)]">
          PSTInsight
        </span>
      </div>

      <button
        onClick={onOpen}
        className="flex items-center gap-1.5 rounded-md bg-[var(--color-accent)] px-3 py-1.5 text-[13px] font-medium text-white hover:bg-[var(--color-accent-soft)]"
      >
        <FolderOpen size={14} /> Open PST
      </button>

      {fileName && (
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-[13px] text-[var(--color-muted)]">{fileName}</span>
          {format && (
            <span className="shrink-0 rounded bg-[var(--color-surface-3)] px-1.5 py-0.5 text-[10px] font-medium uppercase text-[var(--color-faint)]">
              {format}
            </span>
          )}
        </div>
      )}

      <div className="flex flex-1 justify-end">
        <div className="flex w-full max-w-md items-center gap-2 rounded-md border border-[var(--color-border)] bg-[var(--color-surface-2)] px-2.5 py-1.5 focus-within:border-[var(--color-accent)]">
          <Search size={14} className="shrink-0 text-[var(--color-faint)]" />
          <input
            value={query}
            onChange={(e) => onQueryChange(e.target.value)}
            placeholder="Search subject and sender…"
            disabled={!fileName}
            className="min-w-0 flex-1 bg-transparent text-[13px] text-[var(--color-text)] outline-none placeholder:text-[var(--color-faint)] disabled:cursor-not-allowed"
          />
          {searching && <span className="spin h-3 w-3 rounded-full border-2 border-[var(--color-faint)] border-t-transparent" />}
          <button
            onClick={onToggleDeep}
            title="Also search message bodies (slower)"
            className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase ${
              deep
                ? "bg-[var(--color-accent)] text-white"
                : "bg-[var(--color-surface-3)] text-[var(--color-faint)] hover:text-[var(--color-muted)]"
            }`}
          >
            Body
          </button>
          {query && (
            <button onClick={onClearSearch} className="shrink-0 text-[var(--color-faint)] hover:text-[var(--color-text)]">
              <X size={14} />
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
