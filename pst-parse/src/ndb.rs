//! Node Database (NDB) layer: header, b-tree walks, blocks, data trees, subnodes.
//!
//! Supports both Unicode (64-bit) and ANSI (32-bit) PST files. The two formats
//! differ only in integer widths (BID/BREF/IB) and a handful of structure
//! offsets; those differences are captured in [`Format`] and applied uniformly
//! here. See [MS-PST] sections 2.2 and 2.4.

use std::collections::HashMap;

use crate::encoding;
use crate::error::{Error, Result};
use crate::util;
use crate::Pst;

const MAGIC: u32 = 0x4E444221; // "!BDN"
const PAGE_SIZE: usize = 512;

/// On-disk integer width family of a PST file.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Format {
    /// 32-bit "ANSI" format (Outlook 97–2002).
    Ansi,
    /// 64-bit "Unicode" format (Outlook 2003+).
    Unicode,
}

impl Format {
    /// Width in bytes of a BID/BREF/IB field.
    #[inline]
    fn word(self) -> usize {
        match self {
            Format::Ansi => 4,
            Format::Unicode => 8,
        }
    }

    /// File offset of the BTPAGE entry-count fields (cEnt, cEntMax, cbEnt, cLevel).
    #[inline]
    fn page_count_off(self) -> usize {
        match self {
            Format::Ansi => 496,
            Format::Unicode => 488,
        }
    }
}

/// Parsed PST header fields the rest of the crate needs.
#[derive(Debug, Clone)]
pub(crate) struct Header {
    pub format: Format,
    pub crypt: u8,
    pub nbt_root: u64,
    pub bbt_root: u64,
    #[allow(dead_code)]
    pub eof: u64,
}

/// A node b-tree leaf entry.
#[derive(Debug, Clone, Copy)]
pub(crate) struct NodeEntry {
    #[allow(dead_code)]
    pub nid: u32,
    pub bid_data: u64,
    pub bid_sub: u64,
    #[allow(dead_code)]
    pub parent: u32,
}

/// A subnode leaf entry.
#[derive(Debug, Clone, Copy)]
pub(crate) struct SubEntry {
    pub bid_data: u64,
    pub bid_sub: u64,
}

/// Read an unsigned integer of `width` (4 or 8) bytes as u64.
#[inline]
fn read_word(b: &[u8], off: usize, width: usize) -> Result<u64> {
    match width {
        4 => Ok(util::u32(b, off)? as u64),
        _ => util::u64(b, off),
    }
}

/// Parse the file header, detecting ANSI vs Unicode from `wVer`.
pub(crate) fn parse_header(d: &[u8]) -> Result<Header> {
    if util::u32(d, 0)? != MAGIC {
        return Err(Error::BadMagic);
    }
    let ver = util::u16(d, 10)?;
    let format = if ver >= 0x17 {
        Format::Unicode
    } else {
        Format::Ansi
    };

    match format {
        Format::Unicode => {
            let crypt = util::u8(d, 0x201)?;
            // ROOT at 0xB4; BREF { bid, ib } fields are 8 bytes each.
            let eof = util::u64(d, 0xB4 + 4)?;
            let nbt_root = util::u64(d, 0xB4 + 44)?; // BREFNBT.ib
            let bbt_root = util::u64(d, 0xB4 + 60)?; // BREFBBT.ib
            Ok(Header {
                format,
                crypt,
                nbt_root,
                bbt_root,
                eof,
            })
        }
        Format::Ansi => {
            let crypt = util::u8(d, 461)?;
            // ROOT at 0xA4 (164); BREF { bid, ib } fields are 4 bytes each.
            let eof = util::u32(d, 164 + 4)? as u64;
            let nbt_root = util::u32(d, 164 + 24)? as u64; // BREFNBT.ib (188)
            let bbt_root = util::u32(d, 164 + 32)? as u64; // BREFBBT.ib (196)
            Ok(Header {
                format,
                crypt,
                nbt_root,
                bbt_root,
                eof,
            })
        }
    }
}

#[inline]
fn is_internal(bid: u64) -> bool {
    bid & 0x02 != 0
}

impl Pst {
    #[inline]
    fn word(&self) -> usize {
        self.header.format.word()
    }

    fn bytes(&self) -> &[u8] {
        self.data.as_slice()
    }

    /// Read a 512-byte b-tree page at the given file offset.
    fn page(&self, ib: u64) -> Result<&[u8]> {
        util::bytes(self.bytes(), ib as usize, PAGE_SIZE)
    }

    /// Walk a b-tree page tree, returning the leaf entry whose key matches
    /// `target`. `mask` is applied to both keys before comparison.
    fn btree_find(&self, root_ib: u64, target: u64, mask: u64) -> Result<Vec<u8>> {
        let word = self.word();
        let count_off = self.header.format.page_count_off();
        let mut ib = root_ib;
        loop {
            let page = self.page(ib)?;
            let c_ent = util::u8(page, count_off)? as usize;
            let cb_ent = util::u8(page, count_off + 2)? as usize;
            let c_level = util::u8(page, count_off + 3)?;
            if c_ent == 0 || cb_ent == 0 {
                return Err(Error::Invalid("empty b-tree page"));
            }
            if c_level == 0 {
                // Leaf page: return the entry with the matching key.
                for i in 0..c_ent {
                    let off = i * cb_ent;
                    let key = read_word(page, off, word)? & mask;
                    if key == (target & mask) {
                        return Ok(util::bytes(page, off, cb_ent)?.to_vec());
                    }
                }
                return Err(Error::Invalid("key not found in leaf page"));
            } else {
                // Intermediate: BTENTRY = { btkey, BREF{ bid, ib } }.
                // Child ib lives at offset 2*word (skip key + bid).
                let mut chosen: Option<u64> = None;
                for i in 0..c_ent {
                    let off = i * cb_ent;
                    let key = read_word(page, off, word)? & mask;
                    if key <= (target & mask) {
                        chosen = Some(read_word(page, off + 2 * word, word)?);
                    } else {
                        break;
                    }
                }
                ib = chosen.ok_or(Error::Invalid("no child covers key"))?;
            }
        }
    }

    /// Look up a node in the NBT by its NID.
    pub(crate) fn lookup_node(&self, nid: u32) -> Result<NodeEntry> {
        let word = self.word();
        let raw = self
            .btree_find(self.header.nbt_root, nid as u64, u64::MAX)
            .map_err(|_| Error::NodeNotFound(nid))?;
        // NBTENTRY = { nid, bidData, bidSub, nidParent(4) } at word-aligned offsets.
        Ok(NodeEntry {
            nid: read_word(&raw, 0, word)? as u32,
            bid_data: read_word(&raw, word, word)?,
            bid_sub: read_word(&raw, 2 * word, word)?,
            parent: util::u32(&raw, 3 * word)?,
        })
    }

    /// Look up a block in the BBT, returning (file offset, byte count).
    fn lookup_block(&self, bid: u64) -> Result<(u64, usize)> {
        let word = self.word();
        let raw = self
            .btree_find(self.header.bbt_root, bid, !0x1)
            .map_err(|_| Error::BlockNotFound(bid))?;
        // BBTENTRY = { BREF{ bid, ib }, cb(u16), cRef(u16) }.
        let ib = read_word(&raw, word, word)?;
        let cb = util::u16(&raw, 2 * word)? as usize;
        Ok((ib, cb))
    }

    /// Read a block's raw payload bytes (the `cb` payload, no trailer).
    fn raw_block(&self, bid: u64) -> Result<Vec<u8>> {
        let (ib, cb) = self.lookup_block(bid)?;
        Ok(util::bytes(self.bytes(), ib as usize, cb)?.to_vec())
    }

    /// Read a data tree rooted at `bid` into its ordered list of leaf data
    /// blocks, decoding each leaf with the file's encryption method.
    pub(crate) fn read_data_tree(&self, bid: u64) -> Result<Vec<Vec<u8>>> {
        self.read_data_tree_depth(bid, 0)
    }

    fn read_data_tree_depth(&self, bid: u64, depth: usize) -> Result<Vec<Vec<u8>>> {
        if bid == 0 {
            return Ok(Vec::new());
        }
        if depth > 64 {
            return Err(Error::Invalid("data tree too deep"));
        }
        if is_internal(bid) {
            let word = self.word();
            let raw = self.raw_block(bid)?;
            if util::u8(&raw, 0)? != 0x01 {
                return Err(Error::Invalid("expected XBLOCK/XXBLOCK btype 0x01"));
            }
            let c_ent = util::u16(&raw, 2)? as usize;
            let mut out = Vec::new();
            for i in 0..c_ent {
                let child = read_word(&raw, 8 + i * word, word)?;
                out.extend(self.read_data_tree_depth(child, depth + 1)?);
            }
            Ok(out)
        } else {
            let mut block = self.raw_block(bid)?;
            encoding::decode(self.header.crypt, bid, &mut block)?;
            Ok(vec![block])
        }
    }

    /// Read and concatenate a data tree into a single buffer.
    pub(crate) fn read_data_tree_flat(&self, bid: u64) -> Result<Vec<u8>> {
        let mut out = Vec::new();
        for b in self.read_data_tree(bid)? {
            out.extend_from_slice(&b);
        }
        Ok(out)
    }

    /// Read a subnode b-tree rooted at `bid` into a map of subnode NID -> entry.
    pub(crate) fn read_subnodes(&self, bid: u64) -> Result<HashMap<u32, SubEntry>> {
        let mut map = HashMap::new();
        if bid == 0 {
            return Ok(map);
        }
        self.read_subnodes_into(bid, &mut map, 0)?;
        Ok(map)
    }

    fn read_subnodes_into(
        &self,
        bid: u64,
        map: &mut HashMap<u32, SubEntry>,
        depth: usize,
    ) -> Result<()> {
        if depth > 64 {
            return Err(Error::Invalid("subnode tree too deep"));
        }
        let word = self.word();
        let raw = self.raw_block(bid)?;
        if util::u8(&raw, 0)? != 0x02 {
            return Err(Error::Invalid("expected SLBLOCK/SIBLOCK btype 0x02"));
        }
        let c_level = util::u8(&raw, 1)?;
        let c_ent = util::u16(&raw, 2)? as usize;
        if c_level == 0 {
            // SLENTRY = { nid, bidData, bidSub } (3 words).
            let stride = 3 * word;
            for i in 0..c_ent {
                let off = 8 + i * stride;
                let nid = read_word(&raw, off, word)? as u32;
                let bid_data = read_word(&raw, off + word, word)?;
                let bid_sub = read_word(&raw, off + 2 * word, word)?;
                map.insert(nid, SubEntry { bid_data, bid_sub });
            }
        } else {
            // SIENTRY = { nid, bid } (2 words) -> recurse.
            let stride = 2 * word;
            for i in 0..c_ent {
                let off = 8 + i * stride;
                let child = read_word(&raw, off + word, word)?;
                self.read_subnodes_into(child, map, depth + 1)?;
            }
        }
        Ok(())
    }
}
