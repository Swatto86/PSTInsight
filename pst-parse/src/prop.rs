//! Property values and property-type decoding.
//!
//! Covers the [MS-OXCDATA] property type tags that appear in PST property
//! contexts (PC) and table contexts (TC), plus the well-known PidTag IDs the
//! messaging layer needs.

use crate::error::Result;
use crate::util;

/// A decoded property value.
#[derive(Clone, Debug)]
pub enum Value {
    I16(i16),
    I32(i32),
    I64(i64),
    F32(f32),
    F64(f64),
    Bool(bool),
    /// FILETIME: 100-ns ticks since 1601-01-01 UTC.
    Time(u64),
    String(String),
    Binary(Vec<u8>),
    Guid([u8; 16]),
    MultiI32(Vec<i32>),
    MultiString(Vec<String>),
    MultiBinary(Vec<Vec<u8>>),
    Null,
}

// Well-known property IDs (the high 16 bits of a PidTag).
pub const PID_DISPLAY_NAME: u16 = 0x3001;
pub const PID_CONTENT_COUNT: u16 = 0x3602;
pub const PID_UNREAD_COUNT: u16 = 0x3603;
pub const PID_SUBFOLDERS: u16 = 0x360A;
pub const PID_SUBJECT: u16 = 0x0037;
pub const PID_SENDER_NAME: u16 = 0x0C1A;
pub const PID_SENDER_EMAIL: u16 = 0x0C1F;
pub const PID_DISPLAY_TO: u16 = 0x0E04;
pub const PID_MESSAGE_CLASS: u16 = 0x001A;
pub const PID_BODY: u16 = 0x1000;
pub const PID_BODY_HTML: u16 = 0x1013;
pub const PID_RTF_COMPRESSED: u16 = 0x1009;
pub const PID_INTERNET_CODEPAGE: u16 = 0x3FDE;
pub const PID_DELIVERY_TIME: u16 = 0x0E06;
pub const PID_SUBMIT_TIME: u16 = 0x0039;
pub const PID_EMAIL_ADDRESS: u16 = 0x3003;
pub const PID_RECIPIENT_TYPE: u16 = 0x0C15;
pub const PID_SMTP_ADDRESS: u16 = 0x39FE;
pub const PID_ATTACH_FILENAME: u16 = 0x3704;
pub const PID_ATTACH_LONG_FILENAME: u16 = 0x3707;
pub const PID_ATTACH_SIZE: u16 = 0x0E20;
/// Dual-purpose: PtypBinary for by-value attachments, PtypObject for embedded.
pub const PID_ATTACH_DATA_BIN: u16 = 0x3701;
pub const PID_ATTACH_METHOD: u16 = 0x3705;
pub const PID_ATTACH_MIME_TAG: u16 = 0x370E;
pub const PID_ATTACH_CONTENT_ID: u16 = 0x3712;
pub const PID_ATTACH_EXTENSION: u16 = 0x3703;
pub const PID_DISPLAY_CC: u16 = 0x0E03;
pub const PID_DISPLAY_BCC: u16 = 0x0E02;
pub const PID_MESSAGE_FLAGS: u16 = 0x0E07;
pub const PID_MESSAGE_SIZE: u16 = 0x0E08;
pub const PID_IMPORTANCE: u16 = 0x0017;
pub const PID_CREATION_TIME: u16 = 0x3007;
pub const PID_LAST_MODIFICATION_TIME: u16 = 0x3008;
pub const PID_TRANSPORT_HEADERS: u16 = 0x007D;
pub const PID_INTERNET_MESSAGE_ID: u16 = 0x1035;
pub const PID_SENDER_SMTP: u16 = 0x5D01;
pub const PID_SENT_REPRESENTING_SMTP: u16 = 0x5D02;

// Property IDs within the name-to-id map node (NID 0x61).
pub const PID_NAMEID_STREAM_GUID: u16 = 0x0002;
pub const PID_NAMEID_STREAM_ENTRY: u16 = 0x0003;
pub const PID_NAMEID_STREAM_STRING: u16 = 0x0004;

/// Attach method (PidTagAttachMethod) value for an embedded message object.
pub const ATTACH_METHOD_EMBEDDED: i32 = 5;

/// Message flag bit: the message has been read.
pub const MSG_FLAG_READ: i32 = 0x01;
/// Message flag bit: the message has attachments.
pub const MSG_FLAG_HAS_ATTACH: i32 = 0x10;

/// True if an HNID refers to a heap item (HID) rather than an NID subnode.
/// HIDs have their low 5 bits (the type) equal to zero and are non-zero.
#[inline]
pub fn is_hid(hnid: u32) -> bool {
    hnid != 0 && (hnid & 0x1F) == 0
}

/// True if a property type is stored as variable-length data.
#[inline]
pub fn is_variable(prop_type: u16) -> bool {
    matches!(prop_type, 0x1E | 0x1F | 0x102 | 0x0D) || (prop_type & 0x1000) != 0
}

/// True if a PC fixed property is small enough to be stored inline in the
/// property record's HNID slot (<= 4 bytes).
#[inline]
pub fn pc_is_inline(prop_type: u16) -> bool {
    matches!(prop_type, 0x02 | 0x03 | 0x04 | 0x0A | 0x0B)
}

/// Decode a fixed-width property value from its raw little-endian bytes.
pub fn decode_fixed(prop_type: u16, data: &[u8]) -> Result<Value> {
    Ok(match prop_type {
        0x02 => Value::I16(util::u16(data, 0)? as i16),
        0x03 | 0x0A => Value::I32(util::u32(data, 0)? as i32),
        0x04 => Value::F32(f32::from_bits(util::u32(data, 0)?)),
        0x05 | 0x07 => Value::F64(f64::from_bits(util::u64(data, 0)?)),
        0x06 | 0x14 => Value::I64(util::u64(data, 0)? as i64),
        0x40 => Value::Time(util::u64(data, 0)?),
        0x0B => Value::Bool(util::u8(data, 0)? != 0),
        0x48 => {
            let s = util::bytes(data, 0, 16)?;
            let mut g = [0u8; 16];
            g.copy_from_slice(s);
            Value::Guid(g)
        }
        _ => Value::Binary(data.to_vec()),
    })
}

/// Decode a variable-length property value from its raw bytes.
pub fn decode_variable(prop_type: u16, data: &[u8]) -> Result<Value> {
    Ok(match prop_type {
        0x1F => Value::String(utf16le(data)),
        0x1E => Value::String(latin1(data)),
        0x102 => Value::Binary(data.to_vec()),
        0x101F => Value::MultiString(parse_mv_var(data)?.iter().map(|e| utf16le(e)).collect()),
        0x101E => Value::MultiString(parse_mv_var(data)?.iter().map(|e| latin1(e)).collect()),
        0x1102 => Value::MultiBinary(
            parse_mv_var(data)?
                .into_iter()
                .map(|e| e.to_vec())
                .collect(),
        ),
        0x1003 => Value::MultiI32(parse_mv_i32(data)?),
        _ => Value::Binary(data.to_vec()),
    })
}

/// Decode UTF-16LE bytes, lossily.
fn utf16le(data: &[u8]) -> String {
    let units: Vec<u16> = data
        .chunks_exact(2)
        .map(|c| u16::from_le_bytes([c[0], c[1]]))
        .collect();
    String::from_utf16_lossy(&units)
}

/// Decode Latin-1 (ISO-8859-1) bytes — each byte is its own code point.
fn latin1(data: &[u8]) -> String {
    data.iter().map(|&b| b as char).collect()
}

/// Parse a multi-valued variable-length structure into element slices.
/// Layout: u32 count, then `count` u32 offsets (relative to start of struct),
/// then the element bytes. Each element runs to the next offset (or the end).
fn parse_mv_var(data: &[u8]) -> Result<Vec<&[u8]>> {
    let count = util::u32(data, 0)? as usize;
    let mut out = Vec::with_capacity(count);
    for i in 0..count {
        let start = util::u32(data, 4 + i * 4)? as usize;
        let end = if i + 1 < count {
            util::u32(data, 4 + (i + 1) * 4)? as usize
        } else {
            data.len()
        };
        let end = end.max(start).min(data.len());
        out.push(util::bytes(data, start, end - start)?);
    }
    Ok(out)
}

/// Parse a multi-valued i32 structure: u32 count, then `count` little-endian i32.
fn parse_mv_i32(data: &[u8]) -> Result<Vec<i32>> {
    let count = util::u32(data, 0)? as usize;
    let mut out = Vec::with_capacity(count);
    for i in 0..count {
        out.push(util::u32(data, 4 + i * 4)? as i32);
    }
    Ok(out)
}

/// Convert a Windows FILETIME (100-ns ticks since 1601) to a Unix timestamp
/// in seconds. Returns a best-effort value; negative for pre-1970 times.
pub fn filetime_to_unix(ft: u64) -> i64 {
    (ft as i64 - 116_444_736_000_000_000) / 10_000_000
}
