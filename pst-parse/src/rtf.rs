//! Compressed RTF (LZFu) decompression and HTML de-encapsulation.
//!
//! `PidTagRtfCompressed` holds the message body as LZFu-compressed RTF
//! ([MS-OXRTFCP]). When the original body was HTML, Outlook encapsulates it in
//! RTF ([MS-OXRTFEX]); [`deencapsulate_html`] recovers it.

use crate::error::{Error, Result};
use crate::util;

/// The 207-byte LZFu pre-initialised dictionary ([MS-OXRTFCP] 3.1.1.1).
const INIT_DICT: &[u8] = b"{\\rtf1\\ansi\\mac\\deff0\\deftab720{\\fonttbl;}{\\f0\\fnil \\froman \\fswiss \\fmodern \\fscript \\fdecor MS Sans SerifSymbolArialTimes New RomanCourier{\\colortbl\\red0\\green0\\blue0\r\n\\par \\pard\\plain\\f0\\fs20\\b\\i\\u\\tab\\tx";

const DICT_SIZE: usize = 4096;
const MAGIC_COMPRESSED: u32 = 0x75465A4C; // "LZFu"
const MAGIC_UNCOMPRESSED: u32 = 0x414C454D; // "MELA"

/// Decompress a `PidTagRtfCompressed` blob into raw RTF bytes.
pub(crate) fn decompress(input: &[u8]) -> Result<Vec<u8>> {
    let raw_size = util::u32(input, 4)? as usize;
    let magic = util::u32(input, 8)?;
    let body = util::bytes(input, 16, input.len().saturating_sub(16))?;

    if magic == MAGIC_UNCOMPRESSED {
        return Ok(body.to_vec());
    }
    if magic != MAGIC_COMPRESSED {
        return Err(Error::Invalid("unknown compressed-RTF magic"));
    }

    let mut dict = [0u8; DICT_SIZE];
    dict[..INIT_DICT.len()].copy_from_slice(INIT_DICT);
    let mut write_pos = INIT_DICT.len();

    let mut out = Vec::with_capacity(raw_size);
    let mut i = 0usize;
    'outer: while i < body.len() {
        let control = body[i];
        i += 1;
        for bit in 0..8 {
            if (control >> bit) & 1 == 0 {
                // Literal byte.
                if i >= body.len() {
                    break 'outer;
                }
                let b = body[i];
                i += 1;
                out.push(b);
                dict[write_pos % DICT_SIZE] = b;
                write_pos += 1;
            } else {
                // Dictionary reference (two bytes, big-endian).
                if i + 1 >= body.len() {
                    break 'outer;
                }
                let token = ((body[i] as usize) << 8) | (body[i + 1] as usize);
                i += 2;
                let offset = token >> 4;
                let length = (token & 0xF) + 2;
                if offset == (write_pos % DICT_SIZE) {
                    // Reference to the current position marks end of stream.
                    break 'outer;
                }
                for j in 0..length {
                    let b = dict[(offset + j) % DICT_SIZE];
                    out.push(b);
                    dict[write_pos % DICT_SIZE] = b;
                    write_pos += 1;
                }
            }
            if out.len() >= raw_size {
                break 'outer;
            }
        }
    }
    out.truncate(raw_size);
    Ok(out)
}

/// True if the RTF is HTML-encapsulated (`\fromhtml1`).
pub(crate) fn is_html_encapsulated(rtf: &[u8]) -> bool {
    find(rtf, b"\\fromhtml1").is_some() || find(rtf, b"\\fromhtml ").is_some()
}

/// Recover the original HTML from HTML-encapsulated RTF ([MS-OXRTFEX]).
///
/// Implements the common Outlook output: `\htmlrtf` toggling hides RTF-only
/// runs, `\*\htmltag` destinations and plain text carry the HTML, and the
/// usual control-word/escape handling applies. This is a pragmatic decoder,
/// not a full RTF parser.
pub(crate) fn deencapsulate_html(rtf: &[u8]) -> String {
    let mut out: Vec<u8> = Vec::with_capacity(rtf.len());
    let mut i = 0usize;
    // Stack of `htmlrtf` states for nested groups; current = top.
    let mut htmlrtf_stack: Vec<bool> = vec![false];
    // Depth at which an ignorable destination (\*\...) begins, if any.
    let mut ignore_depth: Option<usize> = None;
    let mut depth = 0usize;

    let cur_htmlrtf = |s: &[bool]| *s.last().unwrap_or(&false);

    while i < rtf.len() {
        let c = rtf[i];
        match c {
            b'{' => {
                depth += 1;
                let top = cur_htmlrtf(&htmlrtf_stack);
                htmlrtf_stack.push(top);
                i += 1;
            }
            b'}' => {
                if let Some(d) = ignore_depth {
                    if depth == d {
                        ignore_depth = None;
                    }
                }
                htmlrtf_stack.pop();
                depth = depth.saturating_sub(1);
                i += 1;
            }
            b'\\' => {
                // Control word, control symbol, or escaped char.
                if i + 1 >= rtf.len() {
                    break;
                }
                let n = rtf[i + 1];
                if n == b'\\' || n == b'{' || n == b'}' {
                    if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) {
                        out.push(n);
                    }
                    i += 2;
                } else if n == b'*' {
                    // \* introduces an ignorable destination unless it's \*\htmltag.
                    let (word, _arg, next) = parse_control_word(rtf, i + 2);
                    if word == b"htmltag" {
                        // Keep the destination content (it is literal HTML).
                        i = next;
                    } else {
                        ignore_depth = Some(depth);
                        i += 2;
                    }
                } else if n == b'\'' {
                    // Hex-escaped byte.
                    if i + 3 < rtf.len() {
                        if let Some(b) = hex2(rtf[i + 2], rtf[i + 3]) {
                            if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) {
                                out.push(b);
                            }
                        }
                        i += 4;
                    } else {
                        i += 2;
                    }
                } else if n.is_ascii_alphabetic() {
                    let (word, arg, next) = parse_control_word(rtf, i + 1);
                    i = next;
                    match word.as_slice() {
                        b"htmlrtf" => {
                            // \htmlrtf / \htmlrtf1 turn on RTF-only mode; \htmlrtf0 off.
                            let on = arg != Some(0);
                            if let Some(top) = htmlrtf_stack.last_mut() {
                                *top = on;
                            }
                        }
                        b"par" | b"line"
                            if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) =>
                        {
                            out.push(b'\n');
                        }
                        b"tab" if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) => {
                            out.push(b'\t');
                        }
                        b"lquote" => emit(&mut out, b"\xe2\x80\x98", &htmlrtf_stack, ignore_depth),
                        b"rquote" => emit(&mut out, b"\xe2\x80\x99", &htmlrtf_stack, ignore_depth),
                        b"ldblquote" => {
                            emit(&mut out, b"\xe2\x80\x9c", &htmlrtf_stack, ignore_depth)
                        }
                        b"rdblquote" => {
                            emit(&mut out, b"\xe2\x80\x9d", &htmlrtf_stack, ignore_depth)
                        }
                        b"endash" => emit(&mut out, b"\xe2\x80\x93", &htmlrtf_stack, ignore_depth),
                        b"emdash" => emit(&mut out, b"\xe2\x80\x94", &htmlrtf_stack, ignore_depth),
                        b"bullet" => emit(&mut out, b"\xe2\x80\xa2", &htmlrtf_stack, ignore_depth),
                        b"u" => {
                            // \uN: Unicode code point (signed 16-bit).
                            if let Some(v) = arg {
                                let cp = (v as i64 & 0xFFFF) as u32;
                                if let Some(ch) = char::from_u32(cp) {
                                    if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) {
                                        let mut b = [0u8; 4];
                                        out.extend_from_slice(ch.encode_utf8(&mut b).as_bytes());
                                    }
                                }
                                // Skip the following fallback byte.
                                if i < rtf.len() && rtf[i] == b' ' {
                                    i += 1;
                                }
                                if i < rtf.len() {
                                    i += 1;
                                }
                            }
                        }
                        _ => {} // other control words: ignored
                    }
                } else {
                    i += 2;
                }
            }
            b'\r' | b'\n' => {
                i += 1; // RTF line breaks are not content
            }
            _ => {
                if ignore_depth.is_none() && !cur_htmlrtf(&htmlrtf_stack) {
                    out.push(c);
                }
                i += 1;
            }
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn emit(out: &mut Vec<u8>, bytes: &[u8], stack: &[bool], ignore: Option<usize>) {
    if ignore.is_none() && !*stack.last().unwrap_or(&false) {
        out.extend_from_slice(bytes);
    }
}

/// Parse a control word starting at `start` (just after the backslash).
/// Returns (word, optional numeric argument, index past the delimiter).
fn parse_control_word(rtf: &[u8], start: usize) -> (Vec<u8>, Option<i32>, usize) {
    let mut j = start;
    while j < rtf.len() && rtf[j].is_ascii_alphabetic() {
        j += 1;
    }
    let word = rtf[start..j].to_vec();
    let mut arg: Option<i32> = None;
    if j < rtf.len() && (rtf[j] == b'-' || rtf[j].is_ascii_digit()) {
        let neg = rtf[j] == b'-';
        if neg {
            j += 1;
        }
        let mut v: i32 = 0;
        while j < rtf.len() && rtf[j].is_ascii_digit() {
            v = v.saturating_mul(10).saturating_add((rtf[j] - b'0') as i32);
            j += 1;
        }
        arg = Some(if neg { -v } else { v });
    }
    // A single trailing space is the delimiter and is consumed.
    if j < rtf.len() && rtf[j] == b' ' {
        j += 1;
    }
    (word, arg, j)
}

fn hex2(a: u8, b: u8) -> Option<u8> {
    Some((hexval(a)? << 4) | hexval(b)?)
}

fn hexval(c: u8) -> Option<u8> {
    match c {
        b'0'..=b'9' => Some(c - b'0'),
        b'a'..=b'f' => Some(c - b'a' + 10),
        b'A'..=b'F' => Some(c - b'A' + 10),
        _ => None,
    }
}

fn find(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack.windows(needle.len()).position(|w| w == needle)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn init_dict_len() {
        assert_eq!(INIT_DICT.len(), 207);
    }
}
