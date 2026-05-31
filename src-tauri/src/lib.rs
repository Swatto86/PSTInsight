//! PSTInsight Tauri backend: opens a PST, serves its folders/messages to the
//! web UI, and exports messages and attachments.

mod commands;
mod dto;
mod export;
mod msg;
mod state;

use std::sync::{Arc, Mutex};

use state::SharedState;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let shared: SharedState = Arc::new(Mutex::new(None));

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(shared)
        .invoke_handler(tauri::generate_handler![
            commands::open_pst,
            commands::close_pst,
            commands::list_messages,
            commands::get_message,
            commands::save_attachment,
            commands::export_message,
            commands::export_folder,
            commands::search,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
