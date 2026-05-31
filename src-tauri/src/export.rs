//! Export a message to one of the supported on-disk formats.

use pst_parse::{filetime_to_unix, Message};

/// A user-selectable export format.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Format {
    Eml,
    Txt,
    Msg,
}

impl Format {
    pub fn parse(s: &str) -> Option<Format> {
        match s.to_ascii_lowercase().as_str() {
            "eml" => Some(Format::Eml),
            "txt" => Some(Format::Txt),
            "msg" => Some(Format::Msg),
            _ => None,
        }
    }

    pub fn extension(self) -> &'static str {
        match self {
            Format::Eml => "eml",
            Format::Txt => "txt",
            Format::Msg => "msg",
        }
    }
}

/// Render a message to bytes in the requested format.
pub fn render(msg: &Message, format: Format) -> Result<Vec<u8>, String> {
    match format {
        Format::Eml => Ok(msg.to_eml().into_bytes()),
        Format::Txt => Ok(to_txt(msg).into_bytes()),
        Format::Msg => crate::msg::to_msg(msg).map_err(|e| e.to_string()),
    }
}

/// A plain-text rendering: a small header block followed by the body.
fn to_txt(msg: &Message) -> String {
    let mut out = String::new();
    let mut line = |k: &str, v: &str| {
        if !v.is_empty() {
            out.push_str(k);
            out.push_str(": ");
            out.push_str(v);
            out.push('\n');
        }
    };
    line("Subject", &msg.subject);
    line("From", &format_addr(&msg.sender_name, &msg.sender_email));
    line("To", &msg.display_to);
    line("Cc", &msg.display_cc);
    if let Some(t) = msg.delivery_time.or(msg.submit_time) {
        line("Date", &format_utc(filetime_to_unix(t)));
    }
    out.push('\n');

    if !msg.body.is_empty() {
        out.push_str(&msg.body);
    } else if !msg.body_html.is_empty() {
        out.push_str(&strip_html(&msg.body_html));
    }
    if !out.ends_with('\n') {
        out.push('\n');
    }
    out
}

fn format_addr(name: &str, email: &str) -> String {
    match (name.is_empty(), email.is_empty()) {
        (false, false) => format!("{name} <{email}>"),
        (true, false) => email.to_string(),
        (false, true) => name.to_string(),
        (true, true) => String::new(),
    }
}

/// A deliberately small tag-stripper for the plain-text fallback. Not a full
/// HTML renderer — collapses tags to spaces and decodes a few entities.
fn strip_html(html: &str) -> String {
    let mut out = String::with_capacity(html.len());
    let mut in_tag = false;
    for ch in html.chars() {
        match ch {
            '<' => in_tag = true,
            '>' => {
                in_tag = false;
                out.push(' ');
            }
            c if !in_tag => out.push(c),
            _ => {}
        }
    }
    out.replace("&nbsp;", " ")
        .replace("&amp;", "&")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&quot;", "\"")
        .replace("&#39;", "'")
}

/// Format Unix seconds as a readable UTC timestamp (`YYYY-MM-DD HH:MM:SS UTC`).
fn format_utc(unix: i64) -> String {
    let days = unix.div_euclid(86_400);
    let secs = unix.rem_euclid(86_400);
    let (h, mi, s) = (secs / 3600, (secs % 3600) / 60, secs % 60);
    let (y, m, d) = civil_from_days(days);
    format!("{y:04}-{m:02}-{d:02} {h:02}:{mi:02}:{s:02} UTC")
}

/// Civil date from days since the Unix epoch (Howard Hinnant's algorithm).
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
