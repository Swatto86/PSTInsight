## Projects

**PSTInsight (Rust rewrite)** — Tauri 2 + React 18 + TypeScript + Tailwind v4
front end; Rust backend in `src-tauri`; pure-Rust read-only PST parser in
`pst-parse` (vendored as a path dependency). Replaces the original C#/WPF
PSTInsight. State: v1.0.0 released 2026-05-31 (GitHub release with MSI + NSIS
installers); core app complete and verified to compile/test; MSG export
implemented and round-trip-tested; pending end-to-end run against a real PST
and an installer run-through of the published v1.0.0 build.

Established patterns:
- Domain types (`pst_parse::Message`, `Folder`, …) never cross the IPC boundary;
  `src-tauri/src/dto.rs` defines serializable DTOs with `From` impls.
- Heavy parsing runs in `tauri::async_runtime::spawn_blocking`; shared state is
  `Arc<Mutex<Option<AppState>>>`, locked per call, never held across `.await`.
- List view uses `Pst::message_summaries` (contents-table read), not per-message
  opens. Detail view opens the message on demand.
- Attachment bytes never serialize to the UI; `save_attachment` streams to disk.
- Untrusted HTML email: DOMPurify + sandboxed iframe (`sandbox=""`) + per-frame
  CSP; remote content blocked by default, opt-in toggle.

Open decisions:
- `.msg` export omits named-property values (emits an empty `__nameid` map);
  fine for standard properties. Revisit if a consumer needs named props.
- Panels are fixed-width (folder 256px, list 380px); resizable splitters not yet
  added.

## Architectural decisions

- 2026-05-31 | PSTInsight | Extend `pst-parse` with an additive
  `message_summaries(&Folder)` reading the contents TC directly | The headline
  requirement was "faster"; `pst.messages()` opens+decodes every message (clones
  attachment bytes), fatal for large folders. The contents table already holds
  the list-view columns.
- 2026-05-31 | PSTInsight | Implement `.msg` export in the app layer
  (`src-tauri/src/msg.rs`) using the `cfb` crate, not in `pst-parse` | Keeps the
  parser dependency-light and read-only; a CFBF *writer* is an export concern.
  `cfb` is a mature, verified compound-file implementation — far lower risk than
  a hand-rolled FAT/mini-FAT/directory writer.
- 2026-05-31 | PSTInsight | Render email HTML in a fully sandboxed iframe with a
  per-frame CSP and default remote-blocking | Email `body_html` is
  attacker-controlled; raw rendering in a Tauri webview is XSS against the IPC
  bridge plus tracking-pixel leakage.
- 2026-05-31 | PSTInsight | Tag-driven release pipeline
  (`.github/workflows/release.yml`) via `tauri-apps/tauri-action`, separate from
  the `ci.yml` verify job | A `v*` tag push builds on `windows-latest` and
  publishes a GitHub release with the MSI + NSIS installers; `__VERSION__` in
  `tagName`/`releaseName` resolves from `tauri.conf.json` so tag and app version
  stay in lockstep. Replaced a stale, asset-less pre-rewrite v1.0.0 release.
- 2026-05-31 | PSTInsight | Fixed `ci.yml` trigger `main` → `master` | The repo
  default branch is `master` and no `main` branch exists, so CI had never run;
  the verify gate was inert until this fix.

## Cross-project patterns

- Tauri builds follow the global CLAUDE.md guidance: `rust-toolchain.toml` pinned
  at repo root (1.95.0); `beforeBuildCommand` wired to `npm run build` (Tailwind
  CSS is bundled by Vite, so no separate asset step); clippy `--all-targets`;
  full `tauri build` for release (not `--lib`).

## Open questions / deferred decisions

- End-to-end verification against a real PST (folders, bodies, attachments,
  EML/TXT/MSG export, MSG opening in Outlook) is pending a user-provided file.
- Resizable panes, cid: inline-image resolution in HTML bodies, and named-
  property MSG export are possible future enhancements.

## Environment constraints

- Windows 11; Node 24 / npm 11 (no pnpm — config aligned to npm); Rust 1.95.0.
