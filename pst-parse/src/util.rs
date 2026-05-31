//! Bounds-checked little-endian readers over byte slices.

use crate::error::{Error, Result};

#[inline]
fn slice(b: &[u8], off: usize, len: usize) -> Result<&[u8]> {
    b.get(off..off + len).ok_or(Error::Truncated(off))
}

#[inline]
pub fn u8(b: &[u8], off: usize) -> Result<u8> {
    b.get(off).copied().ok_or(Error::Truncated(off))
}

#[inline]
pub fn u16(b: &[u8], off: usize) -> Result<u16> {
    let s = slice(b, off, 2)?;
    Ok(u16::from_le_bytes([s[0], s[1]]))
}

#[inline]
pub fn u32(b: &[u8], off: usize) -> Result<u32> {
    let s = slice(b, off, 4)?;
    Ok(u32::from_le_bytes([s[0], s[1], s[2], s[3]]))
}

#[inline]
pub fn u64(b: &[u8], off: usize) -> Result<u64> {
    let s = slice(b, off, 8)?;
    let mut a = [0u8; 8];
    a.copy_from_slice(s);
    Ok(u64::from_le_bytes(a))
}

#[inline]
pub fn bytes(b: &[u8], off: usize, len: usize) -> Result<&[u8]> {
    slice(b, off, len)
}
