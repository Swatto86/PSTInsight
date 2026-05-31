//! Application state: the currently open PST plus a folder lookup map.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use pst_parse::{Folder, Pst};

/// Everything held while a PST file is open.
pub struct AppState {
    pub pst: Pst,
    /// Every folder in the store, keyed by node id, for O(1) lookup from the UI.
    pub folders: HashMap<u32, Folder>,
    /// A friendly name for the store root (file stem, since the root folder
    /// itself has no display name).
    pub root_name: String,
}

/// Shared, interior-mutable handle to the open PST (or `None` when closed).
///
/// An `Arc` so a command can clone it into `spawn_blocking` and run parsing off
/// the UI thread; a `Mutex` because only one PST is open at a time and parsing
/// borrows it immutably but exclusively per call.
pub type SharedState = Arc<Mutex<Option<AppState>>>;

/// Maximum folder-tree depth we will walk, as a guard against pathological files.
pub const MAX_FOLDER_DEPTH: usize = 64;
