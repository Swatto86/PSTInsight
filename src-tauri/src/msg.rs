//! `.msg` (MS-OXMSG) export.
//!
//! A `.msg` file is an OLE/CFBF compound file whose storages and streams carry
//! a message's MAPI properties. We build it with the `cfb` crate (which handles
//! the FAT/mini-FAT/directory machinery) and lay out the MAPI property streams
//! per [MS-OXMSG]:
//!
//! - `__properties_version1.0` — fixed-size property records; for the top-level
//!   message its header also declares recipient/attachment counts.
//! - `__substg1.0_<TAG>` — one stream per variable-length property (strings,
//!   binaries), named by the 8-hex-digit property tag.
//! - `__recip_version1.0_#NNNNNNNN` / `__attach_version1.0_#NNNNNNNN` — one
//!   sub-storage per recipient / attachment, each with its own property stream.
//! - `__nameid_version1.0` — the (here empty) named-property map the format
//!   requires to be present.
//!
//! [MS-OXMSG]: https://learn.microsoft.com/openspecs/office_file_formats/ms-oxmsg/

use std::io::{Cursor, Write};

use cfb::CompoundFile;
use pst_parse::Message;

// ---- MAPI property types ------------------------------------------------
const PT_I32: u16 = 0x0003;
const PT_BOOL: u16 = 0x000B;
const PT_TIME: u16 = 0x0040;
const PT_STRING: u16 = 0x001F; // Unicode (UTF-16LE)
const PT_BINARY: u16 = 0x0102;

// ---- Property IDs we emit ----------------------------------------------
const PID_MESSAGE_CLASS: u16 = 0x001A;
const PID_SUBJECT: u16 = 0x0037;
const PID_IMPORTANCE: u16 = 0x0017;
const PID_CLIENT_SUBMIT_TIME: u16 = 0x0039;
const PID_SENT_REPR_NAME: u16 = 0x0042;
const PID_TRANSPORT_HEADERS: u16 = 0x007D;
const PID_BODY: u16 = 0x1000;
const PID_HTML: u16 = 0x1013; // PR_HTML as PtypBinary
const PID_INTERNET_MESSAGE_ID: u16 = 0x1035;
const PID_DISPLAY_BCC: u16 = 0x0E02;
const PID_DISPLAY_CC: u16 = 0x0E03;
const PID_DISPLAY_TO: u16 = 0x0E04;
const PID_DELIVERY_TIME: u16 = 0x0E06;
const PID_MESSAGE_FLAGS: u16 = 0x0E07;
const PID_ATTACH_SIZE: u16 = 0x0E20;
const PID_CREATION_TIME: u16 = 0x3007;
const PID_LAST_MOD_TIME: u16 = 0x3008;
const PID_DISPLAY_NAME: u16 = 0x3001;
const PID_ADDRTYPE: u16 = 0x3002;
const PID_EMAIL_ADDRESS: u16 = 0x3003;
const PID_OBJECT_TYPE: u16 = 0x0FFE;
const PID_DISPLAY_TYPE: u16 = 0x3900;
const PID_SENDER_NAME: u16 = 0x0C1A;
const PID_SENDER_EMAIL: u16 = 0x0C1F;
const PID_RECIPIENT_TYPE: u16 = 0x0C15;
const PID_ROWID: u16 = 0x3000;
const PID_SMTP_ADDRESS: u16 = 0x39FE;
const PID_INTERNET_CPID: u16 = 0x3FDE;
const PID_ATTACH_METHOD: u16 = 0x3705;
const PID_ATTACH_LONG_FILENAME: u16 = 0x3707;
const PID_ATTACH_FILENAME: u16 = 0x3704;
const PID_ATTACH_EXTENSION: u16 = 0x3703;
const PID_ATTACH_MIME_TAG: u16 = 0x370E;
const PID_ATTACH_CONTENT_ID: u16 = 0x3712;
const PID_ATTACH_DATA: u16 = 0x3701;
const PID_RENDERING_POSITION: u16 = 0x370B;

/// MAPI object types.
const OBJECT_TYPE_MAILUSER: i32 = 6;
const OBJECT_TYPE_ATTACH: i32 = 7;

/// Property attribute flags (readable + writable).
const PROP_FLAGS: u32 = 0x0000_0006;

/// One property to serialize: its full tag, the 8-byte inline value, and — for
/// variable-length types — the bytes of its `__substg1.0_` stream.
struct Prop {
    tag: u32,
    inline: [u8; 8],
    stream: Option<Vec<u8>>,
}

fn tag(id: u16, ty: u16) -> u32 {
    ((id as u32) << 16) | ty as u32
}

fn utf16_nul(s: &str) -> Vec<u8> {
    let mut v: Vec<u8> = s.encode_utf16().flat_map(|u| u.to_le_bytes()).collect();
    v.push(0);
    v.push(0);
    v
}

fn p_string(id: u16, value: &str) -> Prop {
    let stream = utf16_nul(value);
    let mut inline = [0u8; 8];
    inline[..4].copy_from_slice(&(stream.len() as u32).to_le_bytes());
    Prop {
        tag: tag(id, PT_STRING),
        inline,
        stream: Some(stream),
    }
}

fn p_binary(id: u16, value: Vec<u8>) -> Prop {
    let mut inline = [0u8; 8];
    inline[..4].copy_from_slice(&(value.len() as u32).to_le_bytes());
    Prop {
        tag: tag(id, PT_BINARY),
        inline,
        stream: Some(value),
    }
}

fn p_i32(id: u16, value: i32) -> Prop {
    let mut inline = [0u8; 8];
    inline[..4].copy_from_slice(&value.to_le_bytes());
    Prop {
        tag: tag(id, PT_I32),
        inline,
        stream: None,
    }
}

fn p_bool(id: u16, value: bool) -> Prop {
    let mut inline = [0u8; 8];
    inline[0] = value as u8;
    Prop {
        tag: tag(id, PT_BOOL),
        inline,
        stream: None,
    }
}

fn p_time(id: u16, ticks: u64) -> Prop {
    Prop {
        tag: tag(id, PT_TIME),
        inline: ticks.to_le_bytes(),
        stream: None,
    }
}

/// Push a string property only when non-empty (avoids littering empty streams).
fn push_str(props: &mut Vec<Prop>, id: u16, value: &str) {
    if !value.is_empty() {
        props.push(p_string(id, value));
    }
}

/// Render a message as `.msg` bytes.
pub fn to_msg(msg: &Message) -> std::io::Result<Vec<u8>> {
    let mut comp = CompoundFile::create(Cursor::new(Vec::new()))?;

    // Root storage CLSID for an IMessage compound file.
    if let Ok(clsid) = uuid::Uuid::parse_str("00020D0B-0000-0000-C000-000000000046") {
        let _ = comp.set_storage_clsid("/", clsid);
    }

    // ---- Top-level message properties ----
    let mut props = vec![
        p_string(
            PID_MESSAGE_CLASS,
            if msg.message_class.is_empty() {
                "IPM.Note"
            } else {
                &msg.message_class
            },
        ),
        p_i32(PID_MESSAGE_FLAGS, msg.message_flags),
        p_i32(PID_IMPORTANCE, msg.importance),
        p_i32(PID_INTERNET_CPID, 65001), // body bytes below are UTF-8
        p_bool(0x0E1B, !msg.attachments.is_empty()), // PidTagHasAttachments
    ];
    push_str(&mut props, PID_SUBJECT, &msg.subject);
    push_str(&mut props, PID_SENDER_NAME, &msg.sender_name);
    push_str(&mut props, PID_SENT_REPR_NAME, &msg.sender_name);
    push_str(&mut props, PID_SENDER_EMAIL, &msg.sender_email);
    push_str(&mut props, PID_DISPLAY_TO, &msg.display_to);
    push_str(&mut props, PID_DISPLAY_CC, &msg.display_cc);
    push_str(&mut props, PID_DISPLAY_BCC, &msg.display_bcc);
    push_str(&mut props, PID_TRANSPORT_HEADERS, &msg.transport_headers);
    push_str(&mut props, PID_INTERNET_MESSAGE_ID, &msg.internet_message_id);
    push_str(&mut props, PID_BODY, &msg.body);
    if !msg.body_html.is_empty() {
        props.push(p_binary(PID_HTML, msg.body_html.clone().into_bytes()));
    }
    if let Some(t) = msg.delivery_time {
        props.push(p_time(PID_DELIVERY_TIME, t));
    }
    if let Some(t) = msg.submit_time {
        props.push(p_time(PID_CLIENT_SUBMIT_TIME, t));
    }
    if let Some(t) = msg.creation_time {
        props.push(p_time(PID_CREATION_TIME, t));
    }
    if let Some(t) = msg.modification_time {
        props.push(p_time(PID_LAST_MOD_TIME, t));
    }

    let recip_count = msg.recipients.len() as u32;
    let attach_count = msg.attachments.len() as u32;
    let header = top_level_header(recip_count, attach_count);
    write_property_storage(&mut comp, "/", &header, &props)?;

    // ---- Recipients ----
    for (i, r) in msg.recipients.iter().enumerate() {
        let storage = format!("/__recip_version1.0_#{i:08X}");
        comp.create_storage(&storage)?;
        let mut rp = vec![
            p_i32(PID_ROWID, i as i32),
            p_i32(PID_RECIPIENT_TYPE, r.recipient_type),
            p_i32(PID_OBJECT_TYPE, OBJECT_TYPE_MAILUSER),
            p_i32(PID_DISPLAY_TYPE, 0),
            p_string(PID_ADDRTYPE, "SMTP"),
        ];
        let name = if r.name.is_empty() { &r.email } else { &r.name };
        push_str(&mut rp, PID_DISPLAY_NAME, name);
        push_str(&mut rp, PID_EMAIL_ADDRESS, &r.email);
        push_str(&mut rp, PID_SMTP_ADDRESS, &r.smtp);
        write_property_storage(&mut comp, &storage, &SUB_HEADER, &rp)?;
    }

    // ---- Attachments (by-value; embedded messages exported as EML bytes) ----
    for (i, a) in msg.attachments.iter().enumerate() {
        let storage = format!("/__attach_version1.0_#{i:08X}");
        comp.create_storage(&storage)?;

        let (data, filename) = match &a.embedded_message {
            Some(embedded) => (
                embedded.to_eml().into_bytes(),
                format!(
                    "{}.eml",
                    if embedded.subject.is_empty() {
                        "embedded"
                    } else {
                        &embedded.subject
                    }
                ),
            ),
            None => (
                a.data.clone(),
                if a.filename.is_empty() {
                    "attachment".to_string()
                } else {
                    a.filename.clone()
                },
            ),
        };

        let mut ap = vec![
            p_i32(PID_ATTACH_METHOD, 1), // afByValue
            p_i32(PID_OBJECT_TYPE, OBJECT_TYPE_ATTACH),
            p_i32(PID_RENDERING_POSITION, -1),
            p_i32(PID_ATTACH_SIZE, data.len() as i32),
            p_binary(PID_ATTACH_DATA, data),
        ];
        push_str(&mut ap, PID_DISPLAY_NAME, &filename);
        push_str(&mut ap, PID_ATTACH_LONG_FILENAME, &filename);
        push_str(&mut ap, PID_ATTACH_FILENAME, &filename);
        push_str(&mut ap, PID_ATTACH_EXTENSION, &a.extension);
        push_str(&mut ap, PID_ATTACH_MIME_TAG, &a.mime_tag);
        push_str(&mut ap, PID_ATTACH_CONTENT_ID, &a.content_id);
        write_property_storage(&mut comp, &storage, &SUB_HEADER, &ap)?;
    }

    // ---- Required (empty) named-property map ----
    comp.create_storage("/__nameid_version1.0")?;
    for t in ["00020102", "00030102", "00040102"] {
        comp.create_stream(format!("/__nameid_version1.0/__substg1.0_{t}"))?;
    }

    Ok(comp.into_inner().into_inner())
}

/// 8-byte property-stream header for recipient and attachment sub-storages.
const SUB_HEADER: [u8; 8] = [0; 8];

/// 32-byte property-stream header for the top-level message.
fn top_level_header(recipients: u32, attachments: u32) -> [u8; 32] {
    let mut h = [0u8; 32];
    h[8..12].copy_from_slice(&recipients.to_le_bytes()); // next recipient id
    h[12..16].copy_from_slice(&attachments.to_le_bytes()); // next attachment id
    h[16..20].copy_from_slice(&recipients.to_le_bytes()); // recipient count
    h[20..24].copy_from_slice(&attachments.to_le_bytes()); // attachment count
    h
}

/// Write a storage's `__properties_version1.0` record stream plus a
/// `__substg1.0_<TAG>` stream for every variable-length property.
fn write_property_storage(
    comp: &mut CompoundFile<Cursor<Vec<u8>>>,
    storage: &str,
    header: &[u8],
    props: &[Prop],
) -> std::io::Result<()> {
    // Records sorted by tag — the recommended canonical order.
    let mut ordered: Vec<&Prop> = props.iter().collect();
    ordered.sort_by_key(|p| p.tag);

    let mut record = Vec::with_capacity(header.len() + ordered.len() * 16);
    record.extend_from_slice(header);
    for p in &ordered {
        record.extend_from_slice(&p.tag.to_le_bytes());
        record.extend_from_slice(&PROP_FLAGS.to_le_bytes());
        record.extend_from_slice(&p.inline);
    }

    let base = if storage == "/" {
        String::new()
    } else {
        storage.to_string()
    };
    let props_path = format!("{base}/__properties_version1.0");
    comp.create_stream(&props_path)?.write_all(&record)?;

    for p in &ordered {
        if let Some(bytes) = &p.stream {
            let name = format!("{base}/__substg1.0_{:08X}", p.tag);
            comp.create_stream(&name)?.write_all(bytes)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Read;

    use pst_parse::{Attachment, Message, Recipient};

    fn sample() -> Message {
        Message {
            nid: 0x200024,
            subject: "Quarterly Réport".to_string(),
            sender_name: "Alice Example".to_string(),
            sender_email: "alice@example.com".to_string(),
            display_to: "Bob <bob@example.com>".to_string(),
            display_cc: String::new(),
            display_bcc: String::new(),
            message_class: "IPM.Note".to_string(),
            body: "Hello, world.".to_string(),
            body_html: "<p>Hello, <b>world</b>.</p>".to_string(),
            body_rtf: String::new(),
            transport_headers: String::new(),
            internet_message_id: "<abc@example.com>".to_string(),
            delivery_time: Some(132_000_000_000_000_000),
            submit_time: None,
            creation_time: None,
            modification_time: None,
            message_flags: 1,
            message_size: 1024,
            importance: 1,
            recipients: vec![Recipient {
                name: "Bob".to_string(),
                email: "bob@example.com".to_string(),
                smtp: "bob@example.com".to_string(),
                recipient_type: 1,
            }],
            attachments: vec![Attachment {
                filename: "note.txt".to_string(),
                extension: ".txt".to_string(),
                mime_tag: "text/plain".to_string(),
                content_id: String::new(),
                size: 5,
                method: 1,
                data: b"hello".to_vec(),
                embedded_message: None,
                properties: Default::default(),
            }],
            properties: Default::default(),
        }
    }

    fn read_stream(comp: &mut CompoundFile<Cursor<Vec<u8>>>, path: &str) -> Vec<u8> {
        let mut s = comp.open_stream(path).expect("stream exists");
        let mut buf = Vec::new();
        s.read_to_end(&mut buf).unwrap();
        buf
    }

    #[test]
    fn produces_readable_compound_file() {
        let bytes = to_msg(&sample()).expect("render");
        // Re-open with the same library: this validates the CFBF structure
        // (header, FAT, mini-FAT, directory tree) end to end.
        let mut comp = CompoundFile::open(Cursor::new(bytes)).expect("valid compound file");

        // Top-level property record: 32-byte header + N * 16-byte entries.
        let props = read_stream(&mut comp, "/__properties_version1.0");
        assert!(props.len() >= 32);
        assert_eq!((props.len() - 32) % 16, 0);
        // Header declares one recipient and one attachment.
        assert_eq!(u32::from_le_bytes(props[16..20].try_into().unwrap()), 1);
        assert_eq!(u32::from_le_bytes(props[20..24].try_into().unwrap()), 1);

        // Subject substg stream decodes back to the original (UTF-16LE + null).
        let subj_tag = tag(PID_SUBJECT, PT_STRING);
        let subj = read_stream(&mut comp, &format!("/__substg1.0_{subj_tag:08X}"));
        assert_eq!(&subj[subj.len() - 2..], &[0, 0]); // null-terminated
        let units: Vec<u16> = subj[..subj.len() - 2]
            .chunks_exact(2)
            .map(|c| u16::from_le_bytes([c[0], c[1]]))
            .collect();
        assert_eq!(String::from_utf16(&units).unwrap(), "Quarterly Réport");

        // Recipient and attachment sub-storages and their data streams exist.
        let _ = read_stream(&mut comp, "/__recip_version1.0_#00000000/__properties_version1.0");
        let data_tag = tag(PID_ATTACH_DATA, PT_BINARY);
        let att = read_stream(
            &mut comp,
            &format!("/__attach_version1.0_#00000000/__substg1.0_{data_tag:08X}"),
        );
        assert_eq!(att, b"hello");
    }

    /// End-to-end against a real PST. Set `PSTINSIGHT_TEST_PST` and run with
    /// `--ignored`. Exercises the new summary method, EML/TXT/MSG rendering, and
    /// confirms each generated `.msg` re-opens as a valid compound file.
    #[test]
    #[ignore]
    fn pipeline_on_real_pst() {
        use crate::export::{render, Format};
        use pst_parse::Pst;

        let path = std::env::var("PSTINSIGHT_TEST_PST")
            .expect("set PSTINSIGHT_TEST_PST to a .pst path");
        let pst = Pst::open(&path).expect("open pst");
        let root = pst.root_folder().expect("root");

        let mut stack = vec![root];
        let mut folders_with_mail = 0;
        let mut messages_rendered = 0;
        let mut open_failures: Vec<String> = Vec::new();
        while let Some(f) = stack.pop() {
            for sub in pst.subfolders(&f).unwrap_or_default() {
                stack.push(sub);
            }
            let summaries = pst.message_summaries(&f).expect("summaries");
            // The fast summary path must agree with the contents-table ids.
            let ids = pst.message_ids(&f).expect("ids");
            assert_eq!(
                summaries.len(),
                ids.len(),
                "summary/id count mismatch in folder {:?}",
                f.name
            );
            if summaries.is_empty() {
                continue;
            }
            folders_with_mail += 1;

            for s in &summaries {
                let m = match pst.open_message(s.nid) {
                    Ok(m) => m,
                    Err(e) => {
                        open_failures.push(format!(
                            "{}/{:#x} {:?}: {e}",
                            f.name, s.nid, s.subject
                        ));
                        continue;
                    }
                };
                for fmt in [Format::Eml, Format::Txt, Format::Msg] {
                    let bytes = render(&m, fmt).expect("render");
                    assert!(!bytes.is_empty(), "empty {fmt:?} for {:?}", m.subject);
                }
                // The .msg must be a structurally valid compound file.
                let msg_bytes = render(&m, Format::Msg).unwrap();
                let mut comp = CompoundFile::open(Cursor::new(msg_bytes)).expect("valid .msg");
                comp.open_stream("/__properties_version1.0")
                    .expect(".msg has top-level properties");
                messages_rendered += 1;
            }
        }
        assert!(folders_with_mail > 0, "no folders with messages");
        eprintln!(
            "{folders_with_mail} folders with mail, {messages_rendered} messages rendered to eml/txt/msg"
        );
        if !open_failures.is_empty() {
            eprintln!("{} open_message failures:", open_failures.len());
            for f in &open_failures {
                eprintln!("  - {f}");
            }
        }
    }
}
