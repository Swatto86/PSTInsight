//! Messaging layer: message store, folders, messages, recipients, attachments.
//!
//! See [MS-PST] section 2.4.

use std::collections::HashMap;

use crate::error::{Error, Result};
use crate::ltp::{LtpContext, PropertyContext};
use crate::named::NamedPropMap;
use crate::ndb::SubEntry;
use crate::prop::{self, Value};
use crate::Pst;

// Special node IDs.
const NID_NAME_TO_ID_MAP: u32 = 0x61;
const NID_ROOT_FOLDER: u32 = 0x122;
const NID_RECIPIENT_TABLE: u32 = 0x692;
const NID_ATTACHMENT_TABLE: u32 = 0x671;

const NID_TYPE_NORMAL_FOLDER: u32 = 0x02;
const NID_TYPE_HIERARCHY_TABLE: u32 = 0x0D;
const NID_TYPE_CONTENTS_TABLE: u32 = 0x0E;

/// Maximum embedded-message nesting depth, to bound recursion on hostile files.
const MAX_EMBED_DEPTH: usize = 16;

/// Replace a NID's type bits.
#[inline]
fn with_type(nid: u32, t: u32) -> u32 {
    (nid & !0x1F) | t
}

/// Read a text property that may be stored as a string or as codepage-encoded
/// binary (`PidTagBody`/`PidTagBodyHtml` can be either).
fn text_property(pc: &PropertyContext, id: u16, codepage: i32) -> String {
    match pc.get(id) {
        Some(Value::String(s)) => s.clone(),
        Some(Value::Binary(b)) => decode_codepage(b, codepage),
        _ => String::new(),
    }
}

/// Decode bytes in the given Windows codepage. UTF-8 (65001) and Windows-1252
/// (the Western default) are handled exactly; other codepages fall back to
/// Windows-1252, which is correct for ASCII and the Latin-1 range.
fn decode_codepage(bytes: &[u8], codepage: i32) -> String {
    if codepage == 65001 {
        return String::from_utf8_lossy(bytes).into_owned();
    }
    // Windows-1252: identical to Latin-1 except for the 0x80-0x9F range.
    bytes.iter().map(|&b| cp1252_char(b)).collect()
}

#[inline]
fn cp1252_char(b: u8) -> char {
    match b {
        0x80 => '\u{20AC}',
        0x82 => '\u{201A}',
        0x83 => '\u{0192}',
        0x84 => '\u{201E}',
        0x85 => '\u{2026}',
        0x86 => '\u{2020}',
        0x87 => '\u{2021}',
        0x88 => '\u{02C6}',
        0x89 => '\u{2030}',
        0x8A => '\u{0160}',
        0x8B => '\u{2039}',
        0x8C => '\u{0152}',
        0x8E => '\u{017D}',
        0x91 => '\u{2018}',
        0x92 => '\u{2019}',
        0x93 => '\u{201C}',
        0x94 => '\u{201D}',
        0x95 => '\u{2022}',
        0x96 => '\u{2013}',
        0x97 => '\u{2014}',
        0x98 => '\u{02DC}',
        0x99 => '\u{2122}',
        0x9A => '\u{0161}',
        0x9B => '\u{203A}',
        0x9C => '\u{0153}',
        0x9E => '\u{017E}',
        0x9F => '\u{0178}',
        other => other as char,
    }
}

/// Strip the control prefix Outlook stores on some subjects (a `\u{0001}`
/// marker plus a separator) and any leading byte-order mark.
fn normalize_subject(mut subject: String) -> String {
    if subject.starts_with('\u{0001}') {
        subject = subject.chars().skip(2).collect();
    }
    subject.trim_start_matches('\u{feff}').to_string()
}

/// The preferred sender address for a message, trying SMTP forms before the
/// raw `PidTagSenderEmailAddress` (which is often an X.500/EX address).
fn sender_address(pc: &PropertyContext) -> String {
    pc.string(prop::PID_SENDER_SMTP)
        .filter(|s| !s.is_empty())
        .or_else(|| pc.string(prop::PID_SENT_REPRESENTING_SMTP))
        .filter(|s| !s.is_empty())
        .or_else(|| pc.string(prop::PID_SENDER_EMAIL))
        .unwrap_or_default()
}

/// A folder in the message store.
#[derive(Clone, Debug)]
pub struct Folder {
    pub nid: u32,
    pub name: String,
    pub content_count: i32,
    pub unread_count: i32,
    pub has_subfolders: bool,
}

/// A lightweight per-message summary read directly from a folder's contents
/// table, without opening (and fully decoding) each message. This is what the
/// list view of a mail client needs, and is dramatically cheaper than
/// [`Pst::messages`] for large folders.
#[derive(Clone, Debug)]
pub struct MessageSummary {
    pub nid: u32,
    pub subject: String,
    pub sender_name: String,
    pub sender_email: String,
    pub delivery_time: Option<u64>,
    pub message_flags: i32,
    pub message_size: i64,
    pub has_attachments: bool,
    pub importance: i32,
}

impl MessageSummary {
    /// Whether the message has been marked read (PidTagMessageFlags bit 0).
    pub fn is_read(&self) -> bool {
        self.message_flags & prop::MSG_FLAG_READ != 0
    }
}

/// A recipient of a message.
#[derive(Clone, Debug)]
pub struct Recipient {
    pub name: String,
    pub email: String,
    pub smtp: String,
    /// 1 = To, 2 = Cc, 3 = Bcc (PidTagRecipientType).
    pub recipient_type: i32,
}

/// An attachment on a message.
#[derive(Clone, Debug)]
pub struct Attachment {
    pub filename: String,
    pub extension: String,
    pub mime_tag: String,
    pub content_id: String,
    pub size: i64,
    /// PidTagAttachMethod (1 = by value, 5 = embedded message, 6 = OLE, ...).
    pub method: i32,
    /// File bytes for by-value attachments; empty for embedded messages.
    pub data: Vec<u8>,
    /// The decoded message for embedded-message attachments (method 5).
    pub embedded_message: Option<Box<Message>>,
    pub properties: PropertyContext,
}

/// A decoded message.
#[derive(Clone, Debug)]
pub struct Message {
    pub nid: u32,
    pub subject: String,
    pub sender_name: String,
    pub sender_email: String,
    pub display_to: String,
    pub display_cc: String,
    pub display_bcc: String,
    pub message_class: String,
    pub body: String,
    pub body_html: String,
    pub body_rtf: String,
    pub transport_headers: String,
    pub internet_message_id: String,
    pub delivery_time: Option<u64>,
    pub submit_time: Option<u64>,
    pub creation_time: Option<u64>,
    pub modification_time: Option<u64>,
    pub message_flags: i32,
    pub message_size: i64,
    pub importance: i32,
    pub recipients: Vec<Recipient>,
    pub attachments: Vec<Attachment>,
    pub properties: PropertyContext,
}

impl Message {
    /// Whether the message has been marked read (PidTagMessageFlags bit 0).
    pub fn is_read(&self) -> bool {
        self.message_flags & prop::MSG_FLAG_READ != 0
    }

    /// Whether the message has any attachments.
    pub fn has_attachments(&self) -> bool {
        !self.attachments.is_empty() || self.message_flags & prop::MSG_FLAG_HAS_ATTACH != 0
    }

    /// Render the message as an RFC 822 / MIME `.eml` document.
    pub fn to_eml(&self) -> String {
        crate::export::to_eml(self)
    }
}

impl Pst {
    /// Build an LTP context for a node's main data + subnodes.
    fn ltp_context(&self, bid_data: u64, bid_sub: u64) -> Result<LtpContext<'_>> {
        let blocks = self.read_data_tree(bid_data)?;
        let subnodes = self.read_subnodes(bid_sub)?;
        Ok(LtpContext::new(self, blocks, subnodes))
    }

    /// Build an LTP context for a subnode entry.
    fn sub_context(&self, sub: &SubEntry) -> Result<LtpContext<'_>> {
        self.ltp_context(sub.bid_data, sub.bid_sub)
    }

    /// Read a node's Property Context.
    fn read_pc(&self, nid: u32) -> Result<PropertyContext> {
        let node = self.lookup_node(nid)?;
        self.ltp_context(node.bid_data, node.bid_sub)?.parse_pc()
    }

    /// Read a node's Table Context rows.
    fn read_tc(&self, nid: u32) -> Result<Vec<(u32, PropertyContext)>> {
        let node = self.lookup_node(nid)?;
        Ok(self
            .ltp_context(node.bid_data, node.bid_sub)?
            .parse_tc()?
            .rows)
    }

    /// Read a subnode's Table Context rows.
    fn read_sub_tc(&self, sub: &SubEntry) -> Result<Vec<(u32, PropertyContext)>> {
        Ok(self.sub_context(sub)?.parse_tc()?.rows)
    }

    /// The message store property context (PidTag properties on NID 0x21).
    pub fn message_store(&self) -> Result<PropertyContext> {
        self.read_pc(0x21)
    }

    /// The named-property map (resolves property IDs >= 0x8000). Returns an
    /// empty map if the file has no name-to-id node.
    pub fn named_properties(&self) -> Result<NamedPropMap> {
        match self.read_pc(NID_NAME_TO_ID_MAP) {
            Ok(pc) => NamedPropMap::from_pc(&pc),
            Err(Error::NodeNotFound(_)) => Ok(NamedPropMap::empty()),
            Err(e) => Err(e),
        }
    }

    /// The root folder of the store.
    pub fn root_folder(&self) -> Result<Folder> {
        self.open_folder(NID_ROOT_FOLDER)
    }

    /// Open a folder by NID.
    pub fn open_folder(&self, nid: u32) -> Result<Folder> {
        let pc = self.read_pc(with_type(nid, NID_TYPE_NORMAL_FOLDER))?;
        Ok(Folder {
            nid,
            name: pc.string(prop::PID_DISPLAY_NAME).unwrap_or_default(),
            content_count: pc.i32(prop::PID_CONTENT_COUNT).unwrap_or(0),
            unread_count: pc.i32(prop::PID_UNREAD_COUNT).unwrap_or(0),
            has_subfolders: pc.bool(prop::PID_SUBFOLDERS).unwrap_or(false),
        })
    }

    /// List a folder's subfolders.
    pub fn subfolders(&self, folder: &Folder) -> Result<Vec<Folder>> {
        let rows = match self.read_tc(with_type(folder.nid, NID_TYPE_HIERARCHY_TABLE)) {
            Ok(r) => r,
            Err(Error::NodeNotFound(_)) => return Ok(Vec::new()),
            Err(e) => return Err(e),
        };
        let mut out = Vec::new();
        for (row_id, _) in rows {
            if let Ok(f) = self.open_folder(row_id) {
                out.push(f);
            }
        }
        Ok(out)
    }

    /// The message NIDs contained in a folder.
    pub fn message_ids(&self, folder: &Folder) -> Result<Vec<u32>> {
        let rows = match self.read_tc(with_type(folder.nid, NID_TYPE_CONTENTS_TABLE)) {
            Ok(r) => r,
            Err(Error::NodeNotFound(_)) => return Ok(Vec::new()),
            Err(e) => return Err(e),
        };
        Ok(rows.into_iter().map(|(id, _)| id).collect())
    }

    /// Read a lightweight summary of every message in a folder, straight from
    /// the contents table. Far cheaper than [`Pst::messages`] because it never
    /// opens a message node, decodes a body, or loads attachment bytes — use it
    /// to paint a list view, then [`Pst::open_message`] on demand.
    ///
    /// Only the columns the contents table actually materializes are populated;
    /// any absent column falls back to a sensible default.
    pub fn message_summaries(&self, folder: &Folder) -> Result<Vec<MessageSummary>> {
        let rows = match self.read_tc(with_type(folder.nid, NID_TYPE_CONTENTS_TABLE)) {
            Ok(r) => r,
            Err(Error::NodeNotFound(_)) => return Ok(Vec::new()),
            Err(e) => return Err(e),
        };
        let mut out = Vec::with_capacity(rows.len());
        for (nid, pc) in rows {
            let flags = pc.i32(prop::PID_MESSAGE_FLAGS).unwrap_or(0);
            out.push(MessageSummary {
                nid,
                subject: normalize_subject(pc.string(prop::PID_SUBJECT).unwrap_or_default()),
                sender_name: pc.string(prop::PID_SENDER_NAME).unwrap_or_default(),
                sender_email: sender_address(&pc),
                delivery_time: pc.time(prop::PID_DELIVERY_TIME),
                message_flags: flags,
                message_size: pc.i64(prop::PID_MESSAGE_SIZE).unwrap_or(0),
                has_attachments: flags & prop::MSG_FLAG_HAS_ATTACH != 0,
                importance: pc.i32(prop::PID_IMPORTANCE).unwrap_or(1),
            });
        }
        Ok(out)
    }

    /// Read every message in a folder.
    pub fn messages(&self, folder: &Folder) -> Result<Vec<Message>> {
        let mut out = Vec::new();
        for nid in self.message_ids(folder)? {
            out.push(self.open_message(nid)?);
        }
        Ok(out)
    }

    /// Open and decode a single message by NID.
    pub fn open_message(&self, nid: u32) -> Result<Message> {
        let node = self.lookup_node(nid)?;
        self.build_message(nid, node.bid_data, node.bid_sub, 0)
    }

    /// Decode a message from its data/subnode block IDs (shared by top-level
    /// and embedded messages).
    fn build_message(
        &self,
        nid: u32,
        bid_data: u64,
        bid_sub: u64,
        depth: usize,
    ) -> Result<Message> {
        let pc = self.ltp_context(bid_data, bid_sub)?.parse_pc()?;
        let subnodes = self.read_subnodes(bid_sub)?;

        let subject = normalize_subject(pc.string(prop::PID_SUBJECT).unwrap_or_default());

        let codepage = pc.i32(prop::PID_INTERNET_CODEPAGE).unwrap_or(1252);
        let body = text_property(&pc, prop::PID_BODY, codepage);

        // Decompress the RTF body if present; recover HTML from it when the
        // message has no explicit PidTagBodyHtml (the common Outlook case).
        let mut body_rtf = String::new();
        let mut body_html = text_property(&pc, prop::PID_BODY_HTML, codepage);
        if let Some(Value::Binary(comp)) = pc.get(prop::PID_RTF_COMPRESSED) {
            if let Ok(raw) = crate::rtf::decompress(comp) {
                if crate::rtf::is_html_encapsulated(&raw) && body_html.is_empty() {
                    body_html = crate::rtf::deencapsulate_html(&raw);
                }
                body_rtf = String::from_utf8_lossy(&raw).into_owned();
            }
        }

        let recipients = match subnodes.get(&NID_RECIPIENT_TABLE) {
            Some(sub) => self.read_recipients(sub).unwrap_or_default(),
            None => Vec::new(),
        };
        let attachments = if depth < MAX_EMBED_DEPTH {
            match subnodes.get(&NID_ATTACHMENT_TABLE) {
                Some(sub) => self
                    .read_attachments(sub, &subnodes, depth)
                    .unwrap_or_default(),
                None => Vec::new(),
            }
        } else {
            Vec::new()
        };

        Ok(Message {
            nid,
            subject,
            sender_name: pc.string(prop::PID_SENDER_NAME).unwrap_or_default(),
            sender_email: sender_address(&pc),
            display_to: pc.string(prop::PID_DISPLAY_TO).unwrap_or_default(),
            display_cc: pc.string(prop::PID_DISPLAY_CC).unwrap_or_default(),
            display_bcc: pc.string(prop::PID_DISPLAY_BCC).unwrap_or_default(),
            message_class: pc.string(prop::PID_MESSAGE_CLASS).unwrap_or_default(),
            body,
            body_html,
            body_rtf,
            transport_headers: pc.string(prop::PID_TRANSPORT_HEADERS).unwrap_or_default(),
            internet_message_id: pc.string(prop::PID_INTERNET_MESSAGE_ID).unwrap_or_default(),
            delivery_time: pc.time(prop::PID_DELIVERY_TIME),
            submit_time: pc.time(prop::PID_SUBMIT_TIME),
            creation_time: pc.time(prop::PID_CREATION_TIME),
            modification_time: pc.time(prop::PID_LAST_MODIFICATION_TIME),
            message_flags: pc.i32(prop::PID_MESSAGE_FLAGS).unwrap_or(0),
            message_size: pc.i64(prop::PID_MESSAGE_SIZE).unwrap_or(0),
            importance: pc.i32(prop::PID_IMPORTANCE).unwrap_or(1),
            recipients,
            attachments,
            properties: pc,
        })
    }

    fn read_recipients(&self, sub: &SubEntry) -> Result<Vec<Recipient>> {
        let rows = self.read_sub_tc(sub)?;
        let mut out = Vec::new();
        for (_, pc) in rows {
            let email = pc
                .string(prop::PID_SMTP_ADDRESS)
                .filter(|s| !s.is_empty())
                .or_else(|| pc.string(prop::PID_EMAIL_ADDRESS))
                .unwrap_or_default();
            out.push(Recipient {
                name: pc.string(prop::PID_DISPLAY_NAME).unwrap_or_default(),
                email,
                smtp: pc.string(prop::PID_SMTP_ADDRESS).unwrap_or_default(),
                recipient_type: pc.i32(prop::PID_RECIPIENT_TYPE).unwrap_or(0),
            });
        }
        Ok(out)
    }

    fn read_attachments(
        &self,
        table_sub: &SubEntry,
        msg_subnodes: &HashMap<u32, SubEntry>,
        depth: usize,
    ) -> Result<Vec<Attachment>> {
        let rows = self.read_sub_tc(table_sub)?;
        let mut out = Vec::new();
        for (row_id, _) in rows {
            // Each attachment row id is itself a subnode NID holding the
            // attachment's property context.
            let Some(att_sub) = msg_subnodes.get(&row_id) else {
                continue;
            };
            let pc = self.sub_context(att_sub)?.parse_pc()?;
            let method = pc.i32(prop::PID_ATTACH_METHOD).unwrap_or(1);
            let filename = pc
                .string(prop::PID_ATTACH_LONG_FILENAME)
                .filter(|s| !s.is_empty())
                .or_else(|| pc.string(prop::PID_ATTACH_FILENAME))
                .unwrap_or_default();

            let mut data = Vec::new();
            let mut embedded_message = None;

            if method == prop::ATTACH_METHOD_EMBEDDED {
                embedded_message = self
                    .read_embedded_message(&pc, att_sub, depth)
                    .ok()
                    .flatten()
                    .map(Box::new);
            } else if let Some(Value::Binary(b)) = pc.get(prop::PID_ATTACH_DATA_BIN) {
                data = b.clone();
            }

            out.push(Attachment {
                filename,
                extension: pc.string(prop::PID_ATTACH_EXTENSION).unwrap_or_default(),
                mime_tag: pc.string(prop::PID_ATTACH_MIME_TAG).unwrap_or_default(),
                content_id: pc.string(prop::PID_ATTACH_CONTENT_ID).unwrap_or_default(),
                size: pc.i64(prop::PID_ATTACH_SIZE).unwrap_or(0),
                method,
                data,
                embedded_message,
                properties: pc,
            });
        }
        Ok(out)
    }

    /// Resolve an embedded-message attachment: PidTagAttachDataObject holds an
    /// 8-byte { subnode-NID, size } descriptor pointing into the attachment's
    /// own subnode tree.
    fn read_embedded_message(
        &self,
        att_pc: &PropertyContext,
        att_sub: &SubEntry,
        depth: usize,
    ) -> Result<Option<Message>> {
        let Some(Value::Binary(desc)) = att_pc.get(prop::PID_ATTACH_DATA_BIN) else {
            return Ok(None);
        };
        if desc.len() < 4 {
            return Ok(None);
        }
        let embed_nid = u32::from_le_bytes([desc[0], desc[1], desc[2], desc[3]]);
        let att_subnodes = self.read_subnodes(att_sub.bid_sub)?;
        let Some(embed) = att_subnodes.get(&embed_nid) else {
            return Ok(None);
        };
        Ok(Some(self.build_message(
            embed_nid,
            embed.bid_data,
            embed.bid_sub,
            depth + 1,
        )?))
    }
}
