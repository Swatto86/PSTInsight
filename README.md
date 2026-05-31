# PSTInsight

A fast, modern viewer for Microsoft Outlook **PST** files — no Outlook, no MAPI,
no COM. PSTInsight reads the on-disk PST format directly and renders it in a
clean three-pane desktop UI.

This is a ground-up Rust rewrite of the original C#/WPF PSTInsight, built on
[Tauri](https://tauri.app/) with a React + TypeScript front end and the
[`pst-parse`](./pst-parse) parsing engine.

## Features

- **Folder tree** with content and unread counts.
- **Virtualized message list** — folders with tens of thousands of messages
  stay responsive because the list is painted from the folder's contents table,
  not by opening every message.
- **Reading pane** with sender/recipient headers, dates, and the message body.
  HTML bodies render in a **fully sandboxed iframe** (DOMPurify-sanitized, a
  per-frame CSP, and remote images **blocked by default** with an opt-in
  "Load images" button — like a real mail client).
- **Attachments**: save by-value attachments to disk; embedded messages save as
  `.eml`. Bytes stream straight to disk and are never shipped to the UI.
- **Export** a single message or an entire folder to **EML**, **TXT**, or
  **MSG** (`.msg` is written as a real MS-OXMSG compound file).
- **Search** across all folders by subject/sender, with an optional (slower)
  body search.

## Architecture

```
PSTInsight/
├── pst-parse/        # Pure-Rust, read-only PST parser (no Outlook/MAPI)
├── src-tauri/        # Tauri backend: state, IPC commands, exporters
│   └── src/
│       ├── commands.rs   # IPC surface (open, list, get, export, search…)
│       ├── dto.rs        # Serializable DTOs for the UI (no domain leakage)
│       ├── export.rs     # EML/TXT rendering + format dispatch
│       ├── msg.rs        # MS-OXMSG (.msg) compound-file writer
│       └── state.rs      # Shared open-PST state
└── src/              # React + TypeScript + Tailwind UI
    ├── components/   # FolderTree, MessageList, MessagePreview, HtmlBody…
    └── lib/          # Typed IPC client and formatting helpers
```

Design notes:

- Parsing runs on a blocking worker thread so a large PST never stalls the UI.
- The list view uses `pst_parse::Pst::message_summaries`, which reads the
  contents table directly — dramatically cheaper than opening each message.
- Untrusted email HTML is treated as hostile: sanitized, sandboxed, and
  remote-blocked before it ever renders.

## Development

Prerequisites: Node 20+, Rust (pinned in `rust-toolchain.toml`), and the
[Tauri prerequisites](https://tauri.app/start/prerequisites/) for your OS.

```bash
npm install
npm run tauri dev      # run the app with hot-reload
npm run tauri build    # produce a release build + installer
```

Quality gates:

```bash
cargo clippy --all-targets -- -D warnings   # in pst-parse and src-tauri
cargo test                                  # parser + MSG round-trip tests
```

## Limitations

- **Unicode PSTs only.** ANSI (32-bit, Outlook 97–2002) files are rejected with
  a clear error; cyclic-encrypted blocks are likewise unsupported (both inherited
  from `pst-parse`).
- Read-only: PSTInsight never modifies the source file.
- `.msg` export covers standard message/recipient/attachment properties; named
  properties are emitted as an (empty) map.

## License

MIT.
