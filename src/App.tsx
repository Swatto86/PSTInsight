import { useCallback, useEffect, useRef, useState } from "react";
import { FileSearch, FolderOpen, Download, ChevronDown } from "lucide-react";
import {
  api,
  pickDirectory,
  pickPstFile,
  pickSavePath,
  type Attachment,
  type ExportFormat,
  type FolderNode,
  type MessageDetail,
  type MessageSummary,
  type OpenResult,
  type SearchHit,
} from "./lib/api";
import { FolderTree } from "./components/FolderTree";
import { MessageList } from "./components/MessageList";
import { SearchResults } from "./components/SearchResults";
import { MessagePreview } from "./components/MessagePreview";
import { Toolbar } from "./components/Toolbar";

type Toast = { kind: "error" | "info"; text: string };

export default function App() {
  const [store, setStore] = useState<OpenResult | null>(null);
  const [opening, setOpening] = useState(false);

  const [folder, setFolder] = useState<FolderNode | null>(null);
  const [messages, setMessages] = useState<MessageSummary[]>([]);
  const [listLoading, setListLoading] = useState(false);

  const [selectedNid, setSelectedNid] = useState<number | null>(null);
  const [detail, setDetail] = useState<MessageDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const [query, setQuery] = useState("");
  const [deep, setDeep] = useState(false);
  const [searching, setSearching] = useState(false);
  const [hits, setHits] = useState<SearchHit[] | null>(null);

  const [toast, setToast] = useState<Toast | null>(null);
  const reqId = useRef(0);

  const notify = useCallback((t: Toast) => {
    setToast(t);
    setTimeout(() => setToast(null), 4000);
  }, []);

  const fail = useCallback(
    (e: unknown) => notify({ kind: "error", text: String(e) }),
    [notify],
  );

  // ---- Message selection ------------------------------------------------
  const selectMessage = useCallback(
    async (nid: number) => {
      setSelectedNid(nid);
      const id = ++reqId.current;
      setDetailLoading(true);
      try {
        const d = await api.getMessage(nid);
        if (id === reqId.current) setDetail(d);
      } catch (e) {
        fail(e);
      } finally {
        if (id === reqId.current) setDetailLoading(false);
      }
    },
    [fail],
  );

  // ---- Folder selection -------------------------------------------------
  const selectFolder = useCallback(
    async (f: FolderNode | null) => {
      setFolder(f);
      setHits(null);
      setQuery("");
      setSelectedNid(null);
      setDetail(null);
      if (!f) {
        setMessages([]);
        return;
      }
      const id = ++reqId.current;
      setListLoading(true);
      try {
        const list = await api.listMessages(f.nid);
        if (id === reqId.current) setMessages(list);
      } catch (e) {
        fail(e);
      } finally {
        if (id === reqId.current) setListLoading(false);
      }
    },
    [fail],
  );

  // ---- Open file --------------------------------------------------------
  const handleOpen = useCallback(async () => {
    try {
      const path = await pickPstFile();
      if (!path) return;
      setOpening(true);
      setStore(null);
      setFolder(null);
      setMessages([]);
      setDetail(null);
      setSelectedNid(null);
      setQuery("");
      setHits(null);
      const result = await api.openPst(path);
      setStore(result);
      selectFolder(pickInitialFolder(result.root));
    } catch (e) {
      fail(e);
    } finally {
      setOpening(false);
    }
  }, [fail, selectFolder]);

  // ---- Search (debounced) ----------------------------------------------
  useEffect(() => {
    if (!store) return;
    const q = query.trim();
    if (!q) {
      setHits(null);
      setSearching(false);
      return;
    }
    setSearching(true);
    const handle = setTimeout(async () => {
      try {
        const results = await api.search(q, deep);
        setHits(results);
      } catch (e) {
        fail(e);
      } finally {
        setSearching(false);
      }
    }, 300);
    return () => clearTimeout(handle);
  }, [query, deep, store, fail]);

  // ---- Exports & attachments -------------------------------------------
  const exportMessage = useCallback(
    async (format: ExportFormat) => {
      if (!detail) return;
      try {
        const base = safeName(detail.subject || "message");
        const dest = await pickSavePath(`${base}.${format}`, [
          { name: format.toUpperCase(), extensions: [format] },
        ]);
        if (!dest) return;
        await api.exportMessage(detail.nid, format, dest);
        notify({ kind: "info", text: `Exported to ${dest}` });
      } catch (e) {
        fail(e);
      }
    },
    [detail, notify, fail],
  );

  const exportFolder = useCallback(
    async (format: ExportFormat) => {
      if (!folder) return;
      try {
        const dir = await pickDirectory();
        if (!dir) return;
        const count = await api.exportFolder(folder.nid, format, dir);
        notify({ kind: "info", text: `Exported ${count} message(s) to ${dir}` });
      } catch (e) {
        fail(e);
      }
    },
    [folder, notify, fail],
  );

  const saveAttachment = useCallback(
    async (att: Attachment) => {
      if (!detail) return;
      try {
        const name = att.is_embedded
          ? `${safeName(att.embedded_subject || "embedded")}.eml`
          : att.filename || "attachment";
        const dest = await pickSavePath(name);
        if (!dest) return;
        await api.saveAttachment(detail.nid, att.index, dest);
        notify({ kind: "info", text: `Saved ${dest}` });
      } catch (e) {
        fail(e);
      }
    },
    [detail, notify, fail],
  );

  // ---- Render -----------------------------------------------------------
  if (!store) {
    return (
      <div className="flex h-screen flex-col bg-[var(--color-bg)]">
        <Toolbar
          fileName={null}
          format={null}
          query=""
          deep={false}
          searching={false}
          onOpen={handleOpen}
          onQueryChange={() => {}}
          onToggleDeep={() => {}}
          onClearSearch={() => {}}
        />
        <Welcome opening={opening} onOpen={handleOpen} />
        <ToastView toast={toast} />
      </div>
    );
  }

  const searchMode = hits !== null;

  return (
    <div className="flex h-screen flex-col bg-[var(--color-bg)]">
      <Toolbar
        fileName={store.root_name}
        format={store.format}
        query={query}
        deep={deep}
        searching={searching}
        onOpen={handleOpen}
        onQueryChange={setQuery}
        onToggleDeep={() => setDeep((d) => !d)}
        onClearSearch={() => setQuery("")}
      />

      <div className="flex min-h-0 flex-1">
        {/* Folder tree */}
        <aside className="w-64 shrink-0 overflow-y-auto border-r border-[var(--color-border)] bg-[var(--color-surface)]">
          <FolderTree
            root={store.root}
            selectedNid={folder?.nid ?? null}
            onSelect={selectFolder}
          />
        </aside>

        {/* Message list / search results */}
        <section className="flex w-[380px] shrink-0 flex-col border-r border-[var(--color-border)] bg-[var(--color-surface)]">
          <ListHeader
            title={searchMode ? "Search results" : folder?.name ?? ""}
            count={searchMode ? hits!.length : messages.length}
            showExport={!searchMode && messages.length > 0}
            onExportFolder={exportFolder}
          />
          <div className="min-h-0 flex-1">
            {searchMode ? (
              <SearchResults
                hits={hits!}
                selectedNid={selectedNid}
                onSelect={(h) => selectMessage(h.nid)}
              />
            ) : listLoading ? (
              <Centered>Loading…</Centered>
            ) : (
              <MessageList
                messages={messages}
                selectedNid={selectedNid}
                onSelect={(m) => selectMessage(m.nid)}
              />
            )}
          </div>
        </section>

        {/* Preview */}
        <main className="min-w-0 flex-1 bg-[var(--color-surface)]">
          <MessagePreview
            detail={detail}
            loading={detailLoading}
            onExport={exportMessage}
            onSaveAttachment={saveAttachment}
          />
        </main>
      </div>

      <ToastView toast={toast} />
    </div>
  );
}

// ---- Local UI bits ------------------------------------------------------

function ListHeader({
  title,
  count,
  showExport,
  onExportFolder,
}: {
  title: string;
  count: number;
  showExport: boolean;
  onExportFolder: (f: ExportFormat) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <div className="flex h-11 shrink-0 items-center gap-2 border-b border-[var(--color-border)] px-4">
      <span className="flex-1 truncate text-[13px] font-semibold text-[var(--color-text)]">
        {title || "—"}
      </span>
      <span className="shrink-0 text-[11px] text-[var(--color-faint)] tabular-nums">
        {count}
      </span>
      {showExport && (
        <div className="relative">
          <button
            onClick={() => setOpen((o) => !o)}
            onBlur={() => setTimeout(() => setOpen(false), 120)}
            title="Export folder"
            className="flex items-center gap-1 rounded-md border border-[var(--color-border)] bg-[var(--color-surface-3)] px-2 py-1 text-[11px] text-[var(--color-muted)] hover:border-[var(--color-border-strong)]"
          >
            <Download size={12} /> <ChevronDown size={11} />
          </button>
          {open && (
            <div className="absolute right-0 z-10 mt-1 w-44 overflow-hidden rounded-md border border-[var(--color-border-strong)] bg-[var(--color-surface-3)] shadow-xl">
              {(["eml", "msg", "txt"] as ExportFormat[]).map((f) => (
                <button
                  key={f}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => {
                    setOpen(false);
                    onExportFolder(f);
                  }}
                  className="block w-full px-3 py-2 text-left text-[12px] text-[var(--color-muted)] hover:bg-[var(--color-accent)]/20 hover:text-[var(--color-text)]"
                >
                  Export all as .{f}
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function Welcome({ opening, onOpen }: { opening: boolean; onOpen: () => void }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-5 bg-[var(--color-bg)]">
      <FileSearch size={56} className="text-[var(--color-accent)]" />
      <div className="text-center">
        <h1 className="text-xl font-semibold text-[var(--color-text)]">PSTInsight</h1>
        <p className="mt-1 max-w-sm text-sm text-[var(--color-muted)]">
          Open an Outlook PST file to browse its folders and read messages — no Outlook
          required.
        </p>
      </div>
      <button
        onClick={onOpen}
        disabled={opening}
        className="flex items-center gap-2 rounded-lg bg-[var(--color-accent)] px-5 py-2.5 text-sm font-medium text-white hover:bg-[var(--color-accent-soft)] disabled:opacity-60"
      >
        {opening ? (
          <span className="spin h-4 w-4 rounded-full border-2 border-white border-t-transparent" />
        ) : (
          <FolderOpen size={16} />
        )}
        {opening ? "Opening…" : "Open PST file"}
      </button>
    </div>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex h-full items-center justify-center text-sm text-[var(--color-faint)]">
      {children}
    </div>
  );
}

function ToastView({ toast }: { toast: Toast | null }) {
  if (!toast) return null;
  return (
    <div
      className={`pointer-events-none fixed bottom-4 left-1/2 z-50 max-w-lg -translate-x-1/2 rounded-lg border px-4 py-2.5 text-[13px] shadow-xl ${
        toast.kind === "error"
          ? "border-red-500/40 bg-red-950/90 text-red-200"
          : "border-[var(--color-border-strong)] bg-[var(--color-surface-3)] text-[var(--color-text)]"
      }`}
    >
      {toast.text}
    </div>
  );
}

// ---- Helpers ------------------------------------------------------------

/** Prefer an Inbox-like folder under the root, else the first child, else root. */
function pickInitialFolder(root: FolderNode): FolderNode {
  const stack = [...root.children];
  while (stack.length) {
    const f = stack.shift()!;
    if (/inbox/i.test(f.name)) return f;
    stack.push(...f.children);
  }
  return root.children[0] ?? root;
}

function safeName(s: string): string {
  return (
    s
      .replace(/[/\\:*?"<>|]/g, "_")
      .replace(/\s+/g, " ")
      .trim()
      .slice(0, 120) || "message"
  );
}
