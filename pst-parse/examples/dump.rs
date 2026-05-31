//! Walk a PST file and print its folder tree with per-message indicators
//! (read state, attachment and embedded-message counts, body availability).
//!
//! Usage:
//!   cargo run --example dump -- path/to/file.pst
//!   cargo run --example dump -- path/to/file.pst <message-nid> out.eml

use pst_parse::{Folder, Message, Pst};

fn main() {
    let mut args = std::env::args().skip(1);
    let Some(path) = args.next() else {
        eprintln!("usage: dump <file.pst> [message-nid out.eml]");
        std::process::exit(2);
    };

    let pst = match Pst::open(&path) {
        Ok(p) => p,
        Err(e) => {
            eprintln!("error opening {path}: {e}");
            std::process::exit(1);
        }
    };

    // Optional: export a single message to .eml.
    if let (Some(nid), Some(out)) = (args.next(), args.next()) {
        let nid = u32::from_str_radix(nid.trim_start_matches("0x"), 16)
            .or_else(|_| nid.parse())
            .expect("message nid must be decimal or 0x-hex");
        match pst.open_message(nid) {
            Ok(m) => {
                std::fs::write(&out, m.to_eml()).expect("write eml");
                println!("wrote {out} ({:?})", m.subject);
            }
            Err(e) => eprintln!("error reading message {nid:#x}: {e}"),
        }
        return;
    }

    println!("format: {:?}", pst.format());
    if let Ok(np) = pst.named_properties() {
        println!("named properties: {}", np.len());
    }
    match pst.root_folder() {
        Ok(root) => walk(&pst, &root, 0),
        Err(e) => eprintln!("error reading root folder: {e}"),
    }
}

fn walk(pst: &Pst, folder: &Folder, depth: usize) {
    let indent = "  ".repeat(depth);
    let name = if folder.name.is_empty() {
        "<root>"
    } else {
        &folder.name
    };
    println!(
        "{indent}{name} ({} items, {} unread)",
        folder.content_count, folder.unread_count
    );

    if let Ok(messages) = pst.messages(folder) {
        for msg in messages.iter().take(8) {
            println!("{indent}  - {}", describe(msg));
        }
        if messages.len() > 8 {
            println!("{indent}  ... and {} more", messages.len() - 8);
        }
    }

    if let Ok(subs) = pst.subfolders(folder) {
        for sub in subs {
            walk(pst, &sub, depth + 1);
        }
    }
}

fn describe(msg: &Message) -> String {
    let subject = if msg.subject.is_empty() {
        "(no subject)"
    } else {
        &msg.subject
    };
    let mut tags = Vec::new();
    if !msg.is_read() {
        tags.push("unread".to_string());
    }
    let files = msg
        .attachments
        .iter()
        .filter(|a| a.embedded_message.is_none())
        .count();
    if files > 0 {
        tags.push(format!("{files} attachment(s)"));
    }
    let embedded = msg
        .attachments
        .iter()
        .filter(|a| a.embedded_message.is_some())
        .count();
    if embedded > 0 {
        tags.push(format!("{embedded} embedded msg"));
    }
    if !msg.body_html.is_empty() {
        tags.push("html".to_string());
    }
    if tags.is_empty() {
        subject.to_string()
    } else {
        format!("{subject}  [{}]", tags.join(", "))
    }
}
