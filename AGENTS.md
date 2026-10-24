# PSTInsight

Read-only viewer for Outlook PST files with no Outlook, MAPI or COM: folder tree, virtualized
message list, reading pane with sandboxed HTML, attachment saving, EML/TXT/MSG export and search.
A Tauri 2 app with a React 18 + TypeScript + Tailwind v4 front end in `src/`, a Rust backend in
`src-tauri/` and `pst-parse/`, a pure-Rust read-only PST parser vendored as a path dependency.
Built and released on Windows only (CI and release run on `windows-latest`). Public repository
`Swatto86/PSTInsight`, MIT, single branch `master`.

## Build, test, verify

- No verify script. CI (`ci.yml`, push and PR to `master`) runs `npm ci`, then
  `cargo clippy --all-targets -- -D warnings` and `cargo test` in `pst-parse/` and again in
  `src-tauri/`, then `npx tauri build`. Run the same before a commit. CI does not run `cargo fmt`,
  and there are no frontend tests; `npm run build` (`tsc && vite build`) type-checks the UI.
- Dev: `npm install`, then `npm run tauri dev`. Release build: `npm run tauri build`. Always do
  the full build: it runs `beforeBuildCommand` (`npm run build`), while `cargo build --lib` would
  let a broken bundle pass.
- Parser alone: `cargo run --example dump -- path/to/file.pst` in `pst-parse/`.
- Rust is pinned to 1.95.0 in `rust-toolchain.toml` and in both workflows; change all three
  together. CI uses Node 20.
- Release: push a `v*` tag. `release.yml` runs `tauri-apps/tauri-action`, which names the release
  `v__VERSION__` from `src-tauri/tauri.conf.json` and uploads the MSI and NSIS installers, then
  adds `PSTInsight_<version>_x64-portable.exe`. Keep the version equal in `package.json`,
  `src-tauri/tauri.conf.json` and `src-tauri/Cargo.toml`; nothing checks it.

## Where things are

- `src-tauri/src/commands.rs`: the IPC surface (open, list, get, save attachment, export, search).
  `dto.rs`: serializable DTOs. `export.rs`: EML/TXT rendering and format dispatch. `msg.rs`:
  MS-OXMSG writer on the `cfb` crate. `state.rs`: shared open-PST state.
- `src/components/` (FolderTree, MessageList, MessagePreview, HtmlBody, SearchResults, Toolbar);
  `src/lib/api.ts`: typed IPC client.
- `pst-parse/src/`: NDB and LTP layers, messaging, named properties, compressed RTF, EML export.

## Constraints

- Read-only: never write to the source PST.
- `pst_parse` domain types never cross IPC; `dto.rs` converts them with `From` impls.
- Parsing runs in `tauri::async_runtime::spawn_blocking`. State is `Arc<Mutex<Option<AppState>>>`,
  locked per call and never held across an `.await`.
- The list view reads the contents table through `Pst::message_summaries`; a message is opened
  only for the detail view.
- Attachment bytes never go to the UI; `save_attachment` streams them to disk.
- Email HTML is hostile: DOMPurify, an iframe with `sandbox=""`, a per-frame CSP, and remote
  content blocked until the user opts in.
- The `.msg` writer belongs in `src-tauri`, not `pst-parse`, which stays dependency-light.
- The startup white flash is prevented by the window `backgroundColor` in `tauri.conf.json`. Do
  not hide the window and show it after paint: WebView2 pauses rAF while hidden, so it never shows.
- The app stores no settings or user data.

## Open questions

- End-to-end check against a real PST (folders, bodies, attachments, EML/TXT/MSG export, the MSG
  opening in Outlook) is waiting for a user-provided file.
- `.msg` export writes an empty named-property map; revisit if a consumer needs named properties.
- Possible later work: resizable panes, `cid:` inline images in HTML bodies.
- README says ANSI (32-bit) PSTs are rejected, but `pst-parse` parses them on an untested path and
  its `AnsiUnsupported` error is never raised. Cyclic-encrypted blocks do error.

## Where longer material lives

- `README.md`: features, architecture sketch, development, limitations.
- `CONTEXT.md`: patterns, decisions with reasons, open questions. Its state line predates v1.0.2.
- `pst-parse/README.md`: parser API, validation, limitations.
