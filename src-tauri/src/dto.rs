//! Serializable data-transfer objects sent to the web UI.
//!
//! These are deliberately separate from the `pst_parse` domain types: the UI
//! must never receive a `PropertyContext` or raw attachment bytes, and the
//! domain crate should carry no serialization concerns.

use serde::Serialize;

use pst_parse::{filetime_to_unix, Attachment, Folder, Message, MessageSummary, Recipient};

/// Convert an optional Windows FILETIME (100-ns ticks since 1601) to optional
/// Unix seconds for the UI to format.
fn unix(ticks: Option<u64>) -> Option<i64> {
    ticks.map(filetime_to_unix)
}

/// A node in the folder tree.
#[derive(Serialize)]
pub struct FolderNode {
    pub nid: u32,
    pub name: String,
    pub content_count: i32,
    pub unread_count: i32,
    pub children: Vec<FolderNode>,
}

/// Result of opening a PST file.
#[derive(Serialize)]
pub struct OpenResult {
    pub path: String,
    pub format: String,
    pub root_name: String,
    pub root: FolderNode,
}

/// A row in the message list.
#[derive(Serialize)]
pub struct MessageSummaryDto {
    pub nid: u32,
    pub subject: String,
    pub sender_name: String,
    pub sender_email: String,
    pub date: Option<i64>,
    pub is_read: bool,
    pub has_attachments: bool,
    pub size: i64,
    pub importance: i32,
}

impl From<MessageSummary> for MessageSummaryDto {
    fn from(m: MessageSummary) -> Self {
        let is_read = m.is_read();
        MessageSummaryDto {
            nid: m.nid,
            subject: m.subject,
            sender_name: m.sender_name,
            sender_email: m.sender_email,
            date: unix(m.delivery_time),
            is_read,
            has_attachments: m.has_attachments,
            size: m.message_size,
            importance: m.importance,
        }
    }
}

/// A message recipient.
#[derive(Serialize)]
pub struct RecipientDto {
    pub name: String,
    pub email: String,
    /// "To", "Cc", "Bcc", or "" for unknown.
    pub kind: String,
}

impl From<&Recipient> for RecipientDto {
    fn from(r: &Recipient) -> Self {
        let kind = match r.recipient_type {
            1 => "To",
            2 => "Cc",
            3 => "Bcc",
            _ => "",
        };
        RecipientDto {
            name: r.name.clone(),
            email: r.email.clone(),
            kind: kind.to_string(),
        }
    }
}

/// Attachment metadata — never the bytes. Saving routes through a command that
/// streams the bytes straight to disk.
#[derive(Serialize)]
pub struct AttachmentDto {
    /// Index into the message's attachment list (stable for a given message).
    pub index: usize,
    pub filename: String,
    pub extension: String,
    pub mime: String,
    pub size: i64,
    pub is_embedded: bool,
    /// Subject of the embedded message, when this attachment is one.
    pub embedded_subject: Option<String>,
}

impl AttachmentDto {
    fn from_attachment(index: usize, a: &Attachment) -> Self {
        let is_embedded = a.embedded_message.is_some();
        let size = if a.size > 0 {
            a.size
        } else {
            a.data.len() as i64
        };
        AttachmentDto {
            index,
            filename: a.filename.clone(),
            extension: a.extension.clone(),
            mime: a.mime_tag.clone(),
            size,
            is_embedded,
            embedded_subject: a.embedded_message.as_ref().map(|m| m.subject.clone()),
        }
    }
}

/// Full message detail for the preview pane.
#[derive(Serialize)]
pub struct MessageDetailDto {
    pub nid: u32,
    pub subject: String,
    pub sender_name: String,
    pub sender_email: String,
    pub display_to: String,
    pub display_cc: String,
    pub display_bcc: String,
    pub message_class: String,
    pub date: Option<i64>,
    pub creation_time: Option<i64>,
    pub modification_time: Option<i64>,
    pub is_read: bool,
    pub importance: i32,
    pub size: i64,
    /// HTML body if the message has one, already present (unsanitized — the UI
    /// sanitizes and sandboxes before rendering).
    pub body_html: String,
    pub body_text: String,
    pub has_html: bool,
    pub recipients: Vec<RecipientDto>,
    pub attachments: Vec<AttachmentDto>,
}

impl From<&Message> for MessageDetailDto {
    fn from(m: &Message) -> Self {
        MessageDetailDto {
            nid: m.nid,
            subject: m.subject.clone(),
            sender_name: m.sender_name.clone(),
            sender_email: m.sender_email.clone(),
            display_to: m.display_to.clone(),
            display_cc: m.display_cc.clone(),
            display_bcc: m.display_bcc.clone(),
            message_class: m.message_class.clone(),
            date: unix(m.delivery_time.or(m.submit_time)),
            creation_time: unix(m.creation_time),
            modification_time: unix(m.modification_time),
            is_read: m.is_read(),
            importance: m.importance,
            size: m.message_size,
            has_html: !m.body_html.is_empty(),
            body_html: m.body_html.clone(),
            body_text: m.body.clone(),
            recipients: m.recipients.iter().map(RecipientDto::from).collect(),
            attachments: m
                .attachments
                .iter()
                .enumerate()
                .map(|(i, a)| AttachmentDto::from_attachment(i, a))
                .collect(),
        }
    }
}

/// Build a `FolderNode` from a parser `Folder` (children filled in by the caller).
pub fn folder_node(f: &Folder, children: Vec<FolderNode>, name_override: Option<&str>) -> FolderNode {
    let name = match name_override {
        Some(n) => n.to_string(),
        None => f.name.clone(),
    };
    FolderNode {
        nid: f.nid,
        name,
        content_count: f.content_count,
        unread_count: f.unread_count,
        children,
    }
}

/// A hit from search.
#[derive(Serialize)]
pub struct SearchHit {
    pub nid: u32,
    pub folder_nid: u32,
    pub folder_name: String,
    pub subject: String,
    pub sender_name: String,
    pub sender_email: String,
    pub date: Option<i64>,
    pub is_read: bool,
    pub has_attachments: bool,
}
