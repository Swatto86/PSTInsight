//! RFC 822 / MIME `.eml` rendering for messages.

use crate::messaging::{Attachment, Message};
use crate::prop;

/// Render a message as an `.eml` document.
pub(crate) fn to_eml(msg: &Message) -> String {
    let real_attachments: Vec<&Attachment> = msg
        .attachments
        .iter()
        .filter(|a| a.embedded_message.is_none() && !a.data.is_empty())
        .collect();

    let mut out = String::new();
    let mut header = |k: &str, v: &str| {
        if !v.is_empty() {
            out.push_str(k);
            out.push_str(": ");
            out.push_str(v);
            out.push_str("\r\n");
        }
    };

    header("From", &format_addr(&msg.sender_name, &msg.sender_email));
    header("To", &recipients_of(msg, 1, &msg.display_to));
    header("Cc", &recipients_of(msg, 2, &msg.display_cc));
    if let Some(t) = msg.submit_time.or(msg.delivery_time) {
        header("Date", &rfc2822_date(prop::filetime_to_unix(t)));
    }
    header("Subject", &encode_header(&msg.subject));
    header("Message-ID", &msg.internet_message_id);
    out.push_str("MIME-Version: 1.0\r\n");

    let has_html = !msg.body_html.is_empty();
    let has_text = !msg.body.is_empty();

    if real_attachments.is_empty() {
        // Single- or alternative-body message, no attachments.
        append_body(&mut out, has_text, has_html, &msg.body, &msg.body_html);
    } else {
        let boundary = "----=_PSTPart_Mixed_0";
        out.push_str(&format!(
            "Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n"
        ));
        out.push_str(&format!("--{boundary}\r\n"));
        append_body(&mut out, has_text, has_html, &msg.body, &msg.body_html);
        for att in real_attachments {
            out.push_str(&format!("\r\n--{boundary}\r\n"));
            append_attachment(&mut out, att);
        }
        out.push_str(&format!("\r\n--{boundary}--\r\n"));
    }
    out
}

fn append_body(out: &mut String, has_text: bool, has_html: bool, text: &str, html: &str) {
    match (has_text, has_html) {
        (true, true) => {
            let boundary = "----=_PSTPart_Alt_0";
            out.push_str(&format!(
                "Content-Type: multipart/alternative; boundary=\"{boundary}\"\r\n\r\n"
            ));
            out.push_str(&format!("--{boundary}\r\n"));
            out.push_str("Content-Type: text/plain; charset=utf-8\r\n");
            out.push_str("Content-Transfer-Encoding: base64\r\n\r\n");
            out.push_str(&wrap76(&base64_encode(text.as_bytes())));
            out.push_str(&format!("\r\n--{boundary}\r\n"));
            out.push_str("Content-Type: text/html; charset=utf-8\r\n");
            out.push_str("Content-Transfer-Encoding: base64\r\n\r\n");
            out.push_str(&wrap76(&base64_encode(html.as_bytes())));
            out.push_str(&format!("\r\n--{boundary}--\r\n"));
        }
        (_, true) => {
            out.push_str("Content-Type: text/html; charset=utf-8\r\n");
            out.push_str("Content-Transfer-Encoding: base64\r\n\r\n");
            out.push_str(&wrap76(&base64_encode(html.as_bytes())));
            out.push_str("\r\n");
        }
        _ => {
            out.push_str("Content-Type: text/plain; charset=utf-8\r\n");
            out.push_str("Content-Transfer-Encoding: base64\r\n\r\n");
            out.push_str(&wrap76(&base64_encode(text.as_bytes())));
            out.push_str("\r\n");
        }
    }
}

fn append_attachment(out: &mut String, att: &Attachment) {
    let ct = if att.mime_tag.is_empty() {
        "application/octet-stream"
    } else {
        &att.mime_tag
    };
    let name = if att.filename.is_empty() {
        "attachment"
    } else {
        &att.filename
    };
    out.push_str(&format!("Content-Type: {ct}; name=\"{name}\"\r\n"));
    out.push_str("Content-Transfer-Encoding: base64\r\n");
    if !att.content_id.is_empty() {
        out.push_str(&format!("Content-ID: <{}>\r\n", att.content_id));
    }
    out.push_str(&format!(
        "Content-Disposition: attachment; filename=\"{name}\"\r\n\r\n"
    ));
    out.push_str(&wrap76(&base64_encode(&att.data)));
    out.push_str("\r\n");
}

fn format_addr(name: &str, email: &str) -> String {
    match (name.is_empty(), email.is_empty()) {
        (false, false) => format!("{} <{}>", name, email),
        (true, false) => email.to_string(),
        (false, true) => name.to_string(),
        (true, true) => String::new(),
    }
}

/// Build an address list from recipients of a given type, falling back to a
/// pre-formatted display string.
fn recipients_of(msg: &Message, rtype: i32, fallback: &str) -> String {
    let list: Vec<String> = msg
        .recipients
        .iter()
        .filter(|r| r.recipient_type == rtype)
        .map(|r| format_addr(&r.name, &r.email))
        .filter(|s| !s.is_empty())
        .collect();
    if list.is_empty() {
        fallback.to_string()
    } else {
        list.join(", ")
    }
}

/// RFC 2047 encode a header value if it contains non-ASCII characters.
fn encode_header(s: &str) -> String {
    if s.is_ascii() {
        s.to_string()
    } else {
        format!("=?utf-8?B?{}?=", base64_encode(s.as_bytes()))
    }
}

/// Standard base64 (RFC 4648).
fn base64_encode(input: &[u8]) -> String {
    const T: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(input.len().div_ceil(3) * 4);
    for chunk in input.chunks(3) {
        let b0 = chunk[0] as usize;
        let b1 = *chunk.get(1).unwrap_or(&0) as usize;
        let b2 = *chunk.get(2).unwrap_or(&0) as usize;
        out.push(T[b0 >> 2] as char);
        out.push(T[((b0 & 0x3) << 4) | (b1 >> 4)] as char);
        out.push(if chunk.len() > 1 {
            T[((b1 & 0xF) << 2) | (b2 >> 6)] as char
        } else {
            '='
        });
        out.push(if chunk.len() > 2 {
            T[b2 & 0x3F] as char
        } else {
            '='
        });
    }
    out
}

/// Wrap a base64 string to 76-character MIME lines.
fn wrap76(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = String::with_capacity(s.len() + s.len() / 76 + 2);
    for (i, chunk) in bytes.chunks(76).enumerate() {
        if i > 0 {
            out.push_str("\r\n");
        }
        out.push_str(std::str::from_utf8(chunk).unwrap_or(""));
    }
    out
}

/// Format a Unix timestamp (seconds) as an RFC 2822 date in UTC.
fn rfc2822_date(unix: i64) -> String {
    const DOW: [&str; 7] = ["Thu", "Fri", "Sat", "Sun", "Mon", "Tue", "Wed"]; // 1970-01-01 = Thu
    const MON: [&str; 12] = [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    ];
    let days = unix.div_euclid(86_400);
    let secs = unix.rem_euclid(86_400);
    let (h, mi, s) = (secs / 3600, (secs % 3600) / 60, secs % 60);
    let dow = DOW[(days.rem_euclid(7)) as usize];
    let (y, m, d) = civil_from_days(days);
    format!(
        "{dow}, {:02} {} {} {:02}:{:02}:{:02} +0000",
        d,
        MON[(m - 1) as usize],
        y,
        h,
        mi,
        s
    )
}

/// Civil date (year, month 1-12, day 1-31) from days since 1970-01-01.
/// Howard Hinnant's algorithm.
fn civil_from_days(z: i64) -> (i64, i64, i64) {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn base64_known_vectors() {
        assert_eq!(base64_encode(b""), "");
        assert_eq!(base64_encode(b"f"), "Zg==");
        assert_eq!(base64_encode(b"fo"), "Zm8=");
        assert_eq!(base64_encode(b"foo"), "Zm9v");
        assert_eq!(base64_encode(b"foobar"), "Zm9vYmFy");
    }

    #[test]
    fn date_epoch() {
        assert_eq!(rfc2822_date(0), "Thu, 01 Jan 1970 00:00:00 +0000");
        // 2010-06-02 12:00:00 UTC = 1275480000
        assert_eq!(
            rfc2822_date(1_275_480_000),
            "Wed, 02 Jun 2010 12:00:00 +0000"
        );
    }
}
