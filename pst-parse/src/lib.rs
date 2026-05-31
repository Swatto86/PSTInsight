//! A from-scratch parser for Microsoft Outlook PST (Personal Storage Table)
//! files, implementing the layers described in the open [MS-PST] specification:
//!
//! - **NDB** (node database): header, node/block b-trees, blocks, data trees,
//!   subnode trees, and block de-obfuscation.
//! - **LTP** (lists, tables, properties): heap-on-node, b-tree-on-heap,
//!   property contexts and table contexts.
//! - **Messaging**: message store, folder hierarchy, messages, recipients,
//!   attachments (including embedded messages) and the named-property map.
//!
//! Both Unicode (64-bit) and ANSI (32-bit) files are parsed. Block encodings
//! *none* and *permute* are supported; *cyclic* (rare, never the Outlook
//! default) currently errors.
//!
//! Files are memory-mapped by [`Pst::open`], so large PSTs are not read wholly
//! into RAM.
//!
//! # Example
//!
//! ```no_run
//! let pst = pst_parse::Pst::open("archive.pst")?;
//! let root = pst.root_folder()?;
//! for folder in pst.subfolders(&root)? {
//!     println!("{} ({} items)", folder.name, folder.content_count);
//!     for msg in pst.messages(&folder)? {
//!         println!("  {}", msg.subject);
//!     }
//! }
//! # Ok::<(), pst_parse::Error>(())
//! ```

mod encoding;
mod error;
mod export;
mod ltp;
mod messaging;
mod named;
mod ndb;
mod prop;
mod rtf;
mod util;

use std::path::Path;

pub use error::{Error, Result};
pub use ltp::{Property, PropertyContext, TableContext};
pub use messaging::{Attachment, Folder, Message, MessageSummary, Recipient};
pub use named::{NamedId, NamedProp, NamedPropMap, PropSet};
pub use ndb::Format;
pub use prop::{filetime_to_unix, Value};

/// Backing storage for a PST's bytes: either a memory map or an owned buffer.
enum Backing {
    Mapped(memmap2::Mmap),
    Owned(Vec<u8>),
}

impl Backing {
    #[inline]
    fn as_slice(&self) -> &[u8] {
        match self {
            Backing::Mapped(m) => m,
            Backing::Owned(v) => v,
        }
    }
}

/// An open PST file.
pub struct Pst {
    data: Backing,
    header: ndb::Header,
}

impl Pst {
    /// Open and memory-map a PST file from disk.
    pub fn open<P: AsRef<Path>>(path: P) -> Result<Self> {
        let file = std::fs::File::open(path)?;
        // SAFETY: the file is opened read-only; we treat the mapping as an
        // immutable byte slice for the lifetime of the `Pst`.
        let mmap = unsafe { memmap2::Mmap::map(&file)? };
        let header = ndb::parse_header(mmap.as_ref())?;
        Ok(Pst {
            data: Backing::Mapped(mmap),
            header,
        })
    }

    /// Parse a PST file from an in-memory buffer.
    pub fn from_bytes(data: Vec<u8>) -> Result<Self> {
        let header = ndb::parse_header(&data)?;
        Ok(Pst {
            data: Backing::Owned(data),
            header,
        })
    }

    /// The on-disk integer-width format of this file (ANSI or Unicode).
    pub fn format(&self) -> Format {
        self.header.format
    }
}
