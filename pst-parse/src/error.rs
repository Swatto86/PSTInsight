//! Error and result types for the crate.

use thiserror::Error;

/// Errors that can occur while opening or parsing a PST file.
#[derive(Debug, Error)]
pub enum Error {
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),

    #[error("not a valid PST file (bad magic)")]
    BadMagic,

    #[error("ANSI (32-bit) PST files are not supported; only Unicode PSTs (wVer >= 0x17)")]
    AnsiUnsupported,

    #[error(
        "unsupported encryption method: {0} (0=none, 1=permute supported, 2=cyclic unsupported)"
    )]
    UnsupportedEncryption(u8),

    #[error("unexpected end of data at offset {0}")]
    Truncated(usize),

    #[error("node {0:#x} not found in the node b-tree")]
    NodeNotFound(u32),

    #[error("block {0:#x} not found in the block b-tree")]
    BlockNotFound(u64),

    #[error("invalid structure: {0}")]
    Invalid(&'static str),
}

/// Crate result alias.
pub type Result<T> = std::result::Result<T, Error>;
