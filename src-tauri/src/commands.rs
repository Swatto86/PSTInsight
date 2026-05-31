//! Tauri commands: the IPC surface the web UI calls.
//!
//! Parsing work runs on a blocking worker thread (`spawn_blocking`) so a large
//! PST never stalls the UI thread. Each command takes the shared state, locks
//! it, and borrows the open `Pst` for the duration of one call.

use std::collections::{HashMap, HashSet};
use std::path::Path;

use tauri::State;

use pst_parse::{Folder, Pst};

use crate::dto::{
    folder_node, FolderNode, MessageDetailDto, MessageSummaryDto, OpenResult, SearchHit,
};
use crate::export::{self, Format};
use crate::state::{AppState, SharedState, MAX_FOLDER_DEPTH};

/// Cap on search results returned to the UI, to keep the list responsive.
const SEARCH_LIMIT: usize = 1000;

/// Run a closure against the open PST on a blocking thread.
async fn with_app<T, F>(state: &SharedState, f: F) -> Result<T, String>
where
    T: Send + 'static,
    F: FnOnce(&AppState) -> Result<T, String> + Send + 'static,
{
    let state = state.clone();
    tauri::async_runtime::spawn_blocking(move || {
        let guard = state.lock().map_err(|_| "state lock poisoned".to_string())?;
        let app = guard
            .as_ref()
            .ok_or_else(|| "no PST file is open".to_string())?;
        f(app)
    })
    .await
    .map_err(|e| format!("worker task failed: {e}"))?
}

/// Recursively walk the folder hierarchy, recording every folder in `map` and
/// building the UI tree. Guards against depth and revisits.
fn walk_folders(
    pst: &Pst,
    folder: &Folder,
    depth: usize,
    map: &mut HashMap<u32, Folder>,
    visited: &mut HashSet<u32>,
) -> FolderNode {
    map.insert(folder.nid, folder.clone());
    let mut children = Vec::new();
    if depth < MAX_FOLDER_DEPTH {
        if let Ok(subs) = pst.subfolders(folder) {
            for sub in subs {
                if visited.insert(sub.nid) {
                    children.push(walk_folders(pst, &sub, depth + 1, map, visited));
                }
            }
        }
    }
    children.sort_by_key(|a| a.name.to_lowercase());
    folder_node(folder, children, None)
}

/// Open a PST file, build its folder tree, and make it the active store.
#[tauri::command]
pub async fn open_pst(state: State<'_, SharedState>, path: String) -> Result<OpenResult, String> {
    let inner = state.inner().clone();
    tauri::async_runtime::spawn_blocking(move || {
        let pst = Pst::open(&path).map_err(|e| e.to_string())?;
        let root = pst.root_folder().map_err(|e| e.to_string())?;

        let root_name = Path::new(&path)
            .file_stem()
            .and_then(|s| s.to_str())
            .filter(|s| !s.is_empty())
            .unwrap_or("PST")
            .to_string();

        let mut map = HashMap::new();
        let mut visited = HashSet::new();
        visited.insert(root.nid);
        let mut root_node = walk_folders(&pst, &root, 0, &mut map, &mut visited);
        if root_node.name.is_empty() {
            root_node.name = root_name.clone();
        }

        let result = OpenResult {
            path,
            format: format!("{:?}", pst.format()),
            root_name: root_name.clone(),
            root: root_node,
        };

        let mut guard = inner.lock().map_err(|_| "state lock poisoned".to_string())?;
        *guard = Some(AppState {
            pst,
            folders: map,
            root_name,
        });
        Ok(result)
    })
    .await
    .map_err(|e| format!("worker task failed: {e}"))?
}

/// Close the active store, freeing the memory map.
#[tauri::command]
pub async fn close_pst(state: State<'_, SharedState>) -> Result<(), String> {
    let mut guard = state.lock().map_err(|_| "state lock poisoned".to_string())?;
    *guard = None;
    Ok(())
}

/// The lightweight message list for a folder.
#[tauri::command]
pub async fn list_messages(
    state: State<'_, SharedState>,
    folder_nid: u32,
) -> Result<Vec<MessageSummaryDto>, String> {
    with_app(state.inner(), move |app| {
        let folder = app
            .folders
            .get(&folder_nid)
            .ok_or_else(|| "unknown folder".to_string())?;
        let sums = app
            .pst
            .message_summaries(folder)
            .map_err(|e| e.to_string())?;
        Ok(sums.into_iter().map(MessageSummaryDto::from).collect())
    })
    .await
}

/// Full detail for one message (no attachment bytes — those stream via
/// [`save_attachment`]).
#[tauri::command]
pub async fn get_message(
    state: State<'_, SharedState>,
    nid: u32,
) -> Result<MessageDetailDto, String> {
    with_app(state.inner(), move |app| {
        let msg = app.pst.open_message(nid).map_err(|e| e.to_string())?;
        Ok(MessageDetailDto::from(&msg))
    })
    .await
}

/// Write one attachment's bytes to a user-chosen destination path. Embedded
/// messages are written as `.eml`.
#[tauri::command]
pub async fn save_attachment(
    state: State<'_, SharedState>,
    msg_nid: u32,
    index: usize,
    dest_path: String,
) -> Result<(), String> {
    with_app(state.inner(), move |app| {
        let msg = app.pst.open_message(msg_nid).map_err(|e| e.to_string())?;
        let att = msg
            .attachments
            .get(index)
            .ok_or_else(|| "attachment index out of range".to_string())?;
        let bytes: Vec<u8> = match &att.embedded_message {
            Some(embedded) => embedded.to_eml().into_bytes(),
            None => att.data.clone(),
        };
        std::fs::write(&dest_path, bytes).map_err(|e| e.to_string())?;
        Ok(())
    })
    .await
}

/// Export one message to a destination path in the given format.
#[tauri::command]
pub async fn export_message(
    state: State<'_, SharedState>,
    nid: u32,
    format: String,
    dest_path: String,
) -> Result<(), String> {
    let fmt = Format::parse(&format).ok_or_else(|| format!("unknown format: {format}"))?;
    with_app(state.inner(), move |app| {
        let msg = app.pst.open_message(nid).map_err(|e| e.to_string())?;
        let bytes = export::render(&msg, fmt)?;
        std::fs::write(&dest_path, bytes).map_err(|e| e.to_string())?;
        Ok(())
    })
    .await
}

/// Export every message in a folder to a directory, one file per message.
/// Returns the number of messages written.
#[tauri::command]
pub async fn export_folder(
    state: State<'_, SharedState>,
    folder_nid: u32,
    format: String,
    dest_dir: String,
) -> Result<u32, String> {
    let fmt = Format::parse(&format).ok_or_else(|| format!("unknown format: {format}"))?;
    with_app(state.inner(), move |app| {
        let folder = app
            .folders
            .get(&folder_nid)
            .ok_or_else(|| "unknown folder".to_string())?;
        let dir = Path::new(&dest_dir);
        std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;

        // Open and render each message individually so one unparseable item
        // (e.g. a non-mail contact record) cannot abort the whole export.
        let ids = app.pst.message_ids(folder).map_err(|e| e.to_string())?;
        let mut used: HashSet<String> = HashSet::new();
        let mut count = 0u32;
        for nid in ids {
            let msg = match app.pst.open_message(nid) {
                Ok(m) => m,
                Err(_) => continue,
            };
            let bytes = match export::render(&msg, fmt) {
                Ok(b) => b,
                Err(_) => continue,
            };
            let base = sanitize_filename(&msg.subject);
            let mut name = format!("{base}.{}", fmt.extension());
            if !used.insert(name.clone()) {
                name = format!("{base}_{:x}.{}", msg.nid, fmt.extension());
                used.insert(name.clone());
            }
            std::fs::write(dir.join(&name), bytes).map_err(|e| e.to_string())?;
            count += 1;
        }
        Ok(count)
    })
    .await
}

/// Search across all folders. By default matches subject and sender from the
/// (cheap) summary rows; `deep` additionally opens each message to scan its
/// body, which is far slower.
#[tauri::command]
pub async fn search(
    state: State<'_, SharedState>,
    query: String,
    deep: bool,
) -> Result<Vec<SearchHit>, String> {
    with_app(state.inner(), move |app| {
        let needle = query.trim().to_lowercase();
        if needle.is_empty() {
            return Ok(Vec::new());
        }
        let mut hits = Vec::new();
        'outer: for folder in app.folders.values() {
            let folder_name = if folder.name.is_empty() {
                app.root_name.clone()
            } else {
                folder.name.clone()
            };
            let summaries = match app.pst.message_summaries(folder) {
                Ok(s) => s,
                Err(_) => continue,
            };
            for s in summaries {
                let shallow = s.subject.to_lowercase().contains(&needle)
                    || s.sender_name.to_lowercase().contains(&needle)
                    || s.sender_email.to_lowercase().contains(&needle);

                let matched = if shallow {
                    true
                } else if deep {
                    match app.pst.open_message(s.nid) {
                        Ok(m) => {
                            m.body.to_lowercase().contains(&needle)
                                || m.display_to.to_lowercase().contains(&needle)
                                || m.body_html.to_lowercase().contains(&needle)
                        }
                        Err(_) => false,
                    }
                } else {
                    false
                };

                if matched {
                    let is_read = s.is_read();
                    let date = s.delivery_time.map(pst_parse::filetime_to_unix);
                    hits.push(SearchHit {
                        nid: s.nid,
                        folder_nid: folder.nid,
                        folder_name: folder_name.clone(),
                        subject: s.subject,
                        sender_name: s.sender_name,
                        sender_email: s.sender_email,
                        date,
                        is_read,
                        has_attachments: s.has_attachments,
                    });
                    if hits.len() >= SEARCH_LIMIT {
                        break 'outer;
                    }
                }
            }
        }
        Ok(hits)
    })
    .await
}

/// Replace characters illegal in Windows filenames and bound the length.
fn sanitize_filename(name: &str) -> String {
    let mut s: String = name
        .chars()
        .map(|c| match c {
            '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|' => '_',
            c if (c as u32) < 0x20 => '_',
            c => c,
        })
        .collect();
    s = s.trim().trim_matches('.').to_string();
    if s.is_empty() {
        s = "untitled".to_string();
    }
    s.chars().take(120).collect()
}
