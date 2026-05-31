# pst-parse

A from-scratch, dependency-light parser for Microsoft Outlook **PST** (Personal
Storage Table) files, written in Rust. No Outlook, no MAPI, no C libraries — it
reads the on-disk format directly per the open [MS-PST] specification.

It is the parsing backend intended to power a cross-platform PST viewer
(e.g. a Tauri app).

## Features

- **Both file formats**: Unicode (64-bit, Outlook 2003+) and ANSI (32-bit,
  Outlook 97–2002). *See limitations re: ANSI testing.*
- **Memory-mapped**: `Pst::open` maps the file; large archives are not read
  wholly into RAM. `Pst::from_bytes` is available for in-memory use.
- **Folder hierarchy** with display name and content/unread counts.
- **Messages**: subject, sender, To/Cc/Bcc, dates (delivery/submit/created/
  modified), message class, flags (`is_read`), size, importance, and the raw
  `PropertyContext` for anything else.
- **Bodies**: plain text, HTML (including `PidTagBodyHtml` stored as
  codepage-encoded binary), and compressed-RTF decode (LZFu) with HTML
  de-encapsulation for `\fromhtml` RTF. Body is exposed as `body`, `body_html`,
  and `body_rtf`.
- **Recipients** with display name, address, SMTP, and type (To/Cc/Bcc).
- **Attachments**: by-value (raw bytes), plus **embedded messages** (attach
  method 5) decoded recursively into nested `Message`s.
- **Named properties** (IDs ≥ 0x8000): the name-to-id map resolved to
  `{ property set GUID, numeric LID or string name }`.
- **EML export**: `Message::to_eml()` renders RFC 822 / MIME.

## Block encodings

PST data blocks may be obfuscated (`bCryptMethod`):

| Method | Name                  | Status        |
| ------ | --------------------- | ------------- |
| 0      | none                  | supported     |
| 1      | permute / compressible| supported     |
| 2      | cyclic                | **errors** (see limitations) |

## Usage

```toml
[dependencies]
pst-parse = { path = "../pst-parse" }
```

```rust
use pst_parse::Pst;

let pst = Pst::open("archive.pst")?;
println!("format: {:?}", pst.format());

let root = pst.root_folder()?;
for folder in pst.subfolders(&root)? {
    println!("{} ({} items, {} unread)",
        folder.name, folder.content_count, folder.unread_count);

    for msg in pst.messages(&folder)? {
        println!("  {}  from {}", msg.subject, msg.sender_email);
        for att in &msg.attachments {
            if att.embedded_message.is_none() {
                println!("    [attachment] {} ({} bytes)", att.filename, att.data.len());
            }
        }
    }
}
# Ok::<(), pst_parse::Error>(())
```

A worked example is in `examples/dump.rs`:

```bash
cargo run --example dump -- path/to/file.pst
cargo run --example dump -- path/to/file.pst 0x200024 out.eml   # export one message
```

## Public API surface

- `Pst::open`, `Pst::from_bytes`, `Pst::format`
- `Pst::root_folder`, `Pst::open_folder`, `Pst::subfolders`
- `Pst::message_ids`, `Pst::messages`, `Pst::open_message`
- `Pst::message_store`, `Pst::named_properties`
- `Folder`, `Message` (+ `is_read`, `has_attachments`, `to_eml`), `Recipient`,
  `Attachment`
- `PropertyContext` (+ `get`, `keys`, `string`, `i32`, `i64`, `bool`, `time`,
  `binary`), `Property`, `Value`, `TableContext`
- `NamedPropMap`, `NamedProp`, `NamedId`, `PropSet`
- `filetime_to_unix`, `Format`, `Error`, `Result`

## Validation

Verified against a synthetic Unicode PST and a real ~28 MB Enron-corpus mailbox:
2,178 messages parsed with zero errors, all bodies and 168 attachments
(including a 4.8 MB MPEG spanning many blocks) extracted byte-perfect; EML
output round-trips through a standard email parser.

## Limitations (honest)

- **ANSI is implemented from the spec but untested** — no ANSI test file was
  available. The offset/width derivations follow [MS-PST], but treat the ANSI
  path as unverified until run against a real 32-bit PST.
- **Cyclic encryption (method 2) errors.** It needs three specific substitution
  tables that were not verifiable here; shipping unverified tables risks silent
  corruption. It is rare (never Outlook's default).
- **HTML recovery from RTF** covers the common HTML-encapsulated case
  (`\fromhtml`); genuinely formatted RTF is exposed as `body_rtf` rather than
  converted to HTML.
- **MSG export** is not implemented (the CFBF/OLE compound-file writer is a
  separate, larger effort). EML export is provided.
- Read-only. This crate does not write or modify PST files.

## License

MIT. See `LICENSE`.

[MS-PST]: https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-pst/
