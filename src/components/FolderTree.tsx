// Left pane: the expandable folder hierarchy.

import { useState } from "react";
import { ChevronRight, Folder, FolderOpen } from "lucide-react";
import type { FolderNode } from "../lib/api";

interface Props {
  root: FolderNode;
  selectedNid: number | null;
  onSelect: (folder: FolderNode) => void;
}

export function FolderTree({ root, selectedNid, onSelect }: Props) {
  // Root expanded by default.
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set([root.nid]));

  const toggle = (nid: number) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      next.has(nid) ? next.delete(nid) : next.add(nid);
      return next;
    });

  return (
    <div className="py-1.5">
      <FolderRow
        node={root}
        depth={0}
        expanded={expanded}
        toggle={toggle}
        selectedNid={selectedNid}
        onSelect={onSelect}
      />
    </div>
  );
}

interface RowProps {
  node: FolderNode;
  depth: number;
  expanded: Set<number>;
  toggle: (nid: number) => void;
  selectedNid: number | null;
  onSelect: (folder: FolderNode) => void;
}

function FolderRow({ node, depth, expanded, toggle, selectedNid, onSelect }: RowProps) {
  const isOpen = expanded.has(node.nid);
  const isSelected = selectedNid === node.nid;
  const hasChildren = node.children.length > 0;
  const unread = node.unread_count > 0;

  return (
    <>
      <div
        onClick={() => onSelect(node)}
        style={{ paddingLeft: depth * 14 + 8 }}
        className={`group flex cursor-pointer items-center gap-1.5 py-1.5 pr-2 text-sm ${
          isSelected
            ? "bg-[var(--color-accent)]/15 text-[var(--color-text)]"
            : "text-[var(--color-muted)] hover:bg-[var(--color-surface-3)]/60"
        }`}
      >
        <button
          onClick={(e) => {
            e.stopPropagation();
            if (hasChildren) toggle(node.nid);
          }}
          className={`flex h-4 w-4 shrink-0 items-center justify-center rounded ${
            hasChildren ? "hover:bg-[var(--color-border-strong)]" : "opacity-0"
          }`}
        >
          <ChevronRight
            size={13}
            className={`transition-transform ${isOpen ? "rotate-90" : ""}`}
          />
        </button>
        {isOpen && hasChildren ? (
          <FolderOpen size={15} className="shrink-0 text-[var(--color-accent)]" />
        ) : (
          <Folder size={15} className="shrink-0 text-[var(--color-faint)]" />
        )}
        <span className={`flex-1 truncate ${unread ? "font-semibold text-[var(--color-text)]" : ""}`}>
          {node.name}
        </span>
        {unread ? (
          <span className="shrink-0 rounded-full bg-[var(--color-accent)] px-1.5 text-[11px] font-semibold text-white">
            {node.unread_count}
          </span>
        ) : node.content_count > 0 ? (
          <span className="shrink-0 text-[11px] text-[var(--color-faint)] tabular-nums">
            {node.content_count}
          </span>
        ) : null}
      </div>
      {isOpen &&
        node.children.map((child) => (
          <FolderRow
            key={child.nid}
            node={child}
            depth={depth + 1}
            expanded={expanded}
            toggle={toggle}
            selectedNid={selectedNid}
            onSelect={onSelect}
          />
        ))}
    </>
  );
}
