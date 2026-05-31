//! Lists, Tables, and Properties (LTP) layer.
//!
//! Builds the Heap-on-Node (HN), B-Tree-on-Heap (BTH), Property Context (PC),
//! and Table Context (TC) on top of the NDB layer. See [MS-PST] section 2.3.

use std::collections::HashMap;

use crate::error::{Error, Result};
use crate::ndb::SubEntry;
use crate::prop::{self, Value};
use crate::util;
use crate::Pst;

/// A Heap-on-Node: the ordered data blocks of a node plus heap addressing.
pub(crate) struct Hn {
    blocks: Vec<Vec<u8>>,
}

impl Hn {
    fn new(blocks: Vec<Vec<u8>>) -> Self {
        Hn { blocks }
    }

    /// The HID of the heap's user root (HNHDR.hidUserRoot in block 0).
    fn user_root(&self) -> Result<u32> {
        let b0 = self.blocks.first().ok_or(Error::Invalid("empty HN node"))?;
        util::u32(b0, 4)
    }

    /// Resolve a heap ID to its bytes.
    fn resolve(&self, hid: u32) -> Result<&[u8]> {
        // hidIndex is 1-based into the block's allocation table; hidBlockIndex
        // selects which data block.
        let block_index = (hid >> 16) as usize;
        let alloc_index = ((hid >> 5) & 0x7FF) as usize;
        if alloc_index == 0 {
            return Err(Error::Invalid("HID alloc index 0"));
        }
        let block = self
            .blocks
            .get(block_index)
            .ok_or(Error::Invalid("HID block index out of range"))?;
        // Page map offset is at the *start* of every HN block (HNHDR or HNPAGEHDR).
        let ib_hnpm = util::u16(block, 0)? as usize;
        // HNPAGEMAP: cAlloc u16, cFree u16, then (cAlloc+1) u16 offsets.
        let start = util::u16(block, ib_hnpm + 4 + (alloc_index - 1) * 2)? as usize;
        let end = util::u16(block, ib_hnpm + 4 + alloc_index * 2)? as usize;
        if end < start {
            return Err(Error::Invalid("HN allocation end before start"));
        }
        util::bytes(block, start, end - start)
    }
}

/// A live LTP context bound to a PST: heap plus the node's subnodes.
pub(crate) struct LtpContext<'a> {
    pst: &'a Pst,
    hn: Hn,
    subnodes: HashMap<u32, SubEntry>,
}

impl<'a> LtpContext<'a> {
    pub(crate) fn new(
        pst: &'a Pst,
        blocks: Vec<Vec<u8>>,
        subnodes: HashMap<u32, SubEntry>,
    ) -> Self {
        LtpContext {
            pst,
            hn: Hn::new(blocks),
            subnodes,
        }
    }

    /// Resolve an HNID: either a heap item (HID) or a subnode (NID) whose data
    /// tree is read and concatenated.
    fn resolve_hnid(&self, hnid: u32) -> Result<Vec<u8>> {
        if hnid == 0 {
            return Ok(Vec::new());
        }
        if prop::is_hid(hnid) {
            Ok(self.hn.resolve(hnid)?.to_vec())
        } else {
            let sub = self
                .subnodes
                .get(&hnid)
                .ok_or(Error::Invalid("HNID subnode not found"))?;
            self.pst.read_data_tree_flat(sub.bid_data)
        }
    }

    /// Walk a BTH and return its (key, data) leaf records.
    fn bth_records(&self, hid_header: u32) -> Result<Vec<(Vec<u8>, Vec<u8>)>> {
        let hdr = self.hn.resolve(hid_header)?;
        if util::u8(hdr, 0)? != 0xB5 {
            return Err(Error::Invalid("expected BTHHEADER 0xB5"));
        }
        let cb_key = util::u8(hdr, 1)? as usize;
        let cb_ent = util::u8(hdr, 2)? as usize;
        let levels = util::u8(hdr, 3)?;
        let root = util::u32(hdr, 4)?;
        let mut out = Vec::new();
        if root != 0 {
            self.bth_collect(root, levels, cb_key, cb_ent, &mut out)?;
        }
        Ok(out)
    }

    fn bth_collect(
        &self,
        hid: u32,
        levels: u8,
        cb_key: usize,
        cb_ent: usize,
        out: &mut Vec<(Vec<u8>, Vec<u8>)>,
    ) -> Result<()> {
        let node = self.hn.resolve(hid)?;
        if levels == 0 {
            let rec = cb_key + cb_ent;
            if rec == 0 {
                return Err(Error::Invalid("BTH record size 0"));
            }
            for chunk in node.chunks(rec) {
                if chunk.len() < rec {
                    break;
                }
                out.push((chunk[..cb_key].to_vec(), chunk[cb_key..rec].to_vec()));
            }
        } else {
            // Intermediate: key (cb_key) + child HID (u32).
            let rec = cb_key + 4;
            for chunk in node.chunks(rec) {
                if chunk.len() < rec {
                    break;
                }
                let child = u32::from_le_bytes([
                    chunk[cb_key],
                    chunk[cb_key + 1],
                    chunk[cb_key + 2],
                    chunk[cb_key + 3],
                ]);
                self.bth_collect(child, levels - 1, cb_key, cb_ent, out)?;
            }
        }
        Ok(())
    }

    /// Parse this context as a Property Context (PC).
    pub(crate) fn parse_pc(&self) -> Result<PropertyContext> {
        let user_root = self.hn.user_root()?;
        let records = self.bth_records(user_root)?;
        let mut props = HashMap::new();
        for (key, data) in records {
            let prop_id = u16::from_le_bytes([key[0], key[1]]);
            let prop_type = util::u16(&data, 0)?;
            let hnid = util::u32(&data, 2)?;
            let value = if prop::is_variable(prop_type) {
                let bytes = self.resolve_hnid(hnid)?;
                prop::decode_variable(prop_type, &bytes)?
            } else if prop::pc_is_inline(prop_type) {
                // Value stored inline in the 4-byte HNID slot.
                prop::decode_fixed(prop_type, &hnid.to_le_bytes())?
            } else {
                // 8- or 16-byte fixed value stored as a heap item.
                let bytes = self.resolve_hnid(hnid)?;
                prop::decode_fixed(prop_type, &bytes)?
            };
            props.insert(
                prop_id,
                Property {
                    type_: prop_type,
                    value,
                },
            );
        }
        Ok(PropertyContext { props })
    }

    /// Parse this context as a Table Context (TC), returning all rows.
    pub(crate) fn parse_tc(&self) -> Result<TableContext> {
        let user_root = self.hn.user_root()?;
        let info = self.hn.resolve(user_root)?;
        if util::u8(info, 0)? != 0x7C {
            return Err(Error::Invalid("expected TCINFO 0x7C"));
        }
        let c_cols = util::u8(info, 1)? as usize;
        // rgib: 4 x u16 group offsets. [2] = start of CEB (existence bitmap),
        // [3] = total row width.
        let ceb_start = util::u16(info, 2 + 2 * 2)? as usize;
        let row_size = util::u16(info, 2 + 3 * 2)? as usize;
        let hid_row_index = util::u32(info, 2 + 4 * 2)?;
        let hnid_rows = util::u32(info, 2 + 4 * 2 + 4)?;
        // TCOLDESC array starts at offset 22, each 8 bytes.
        let mut cols = Vec::with_capacity(c_cols);
        for i in 0..c_cols {
            let off = 22 + i * 8;
            let tag = util::u32(info, off)?;
            let ib_data = util::u16(info, off + 4)? as usize;
            let cb_data = util::u8(info, off + 6)? as usize;
            let i_bit = util::u8(info, off + 7)? as usize;
            cols.push(ColDesc {
                prop_id: (tag >> 16) as u16,
                prop_type: (tag & 0xFFFF) as u16,
                ib_data,
                cb_data,
                i_bit,
            });
        }

        // Row index BTH: maps rowId -> row matrix index (we only need the rowIds,
        // in order, to label rows).
        let row_ids: Vec<u32> = self
            .bth_records(hid_row_index)?
            .into_iter()
            .filter_map(|(k, _)| {
                k.get(0..4)
                    .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            })
            .collect();

        // Row matrix: a sequence of fixed-width rows, possibly spread over
        // several data blocks. Rows do not straddle block boundaries.
        let row_blocks: Vec<Vec<u8>> = if hnid_rows == 0 {
            Vec::new()
        } else if prop::is_hid(hnid_rows) {
            vec![self.hn.resolve(hnid_rows)?.to_vec()]
        } else {
            let sub = self
                .subnodes
                .get(&hnid_rows)
                .ok_or(Error::Invalid("TC rows subnode not found"))?;
            self.pst.read_data_tree(sub.bid_data)?
        };

        if row_size == 0 {
            return Err(Error::Invalid("TC row size 0"));
        }

        let mut rows = Vec::new();
        let mut row_counter = 0usize;
        for block in &row_blocks {
            for row in block.chunks(row_size) {
                if row.len() < row_size {
                    break;
                }
                let row_id = row_ids
                    .get(row_counter)
                    .copied()
                    .unwrap_or(row_counter as u32);
                row_counter += 1;
                let pc = self.decode_tc_row(row, ceb_start, &cols)?;
                rows.push((row_id, pc));
            }
        }
        Ok(TableContext { rows })
    }

    fn decode_tc_row(
        &self,
        row: &[u8],
        ceb_start: usize,
        cols: &[ColDesc],
    ) -> Result<PropertyContext> {
        let mut props = HashMap::new();
        let ceb = util::bytes(row, ceb_start, cols.len().div_ceil(8))?;
        for col in cols {
            let byte = col.i_bit / 8;
            let bit = 7 - (col.i_bit % 8);
            let present = (ceb.get(byte).copied().unwrap_or(0) >> bit) & 1 == 1;
            if !present {
                continue;
            }
            let cell = util::bytes(row, col.ib_data, col.cb_data)?;
            let value = if prop::is_variable(col.prop_type) {
                let hnid = util::u32(cell, 0)?;
                let bytes = self.resolve_hnid(hnid)?;
                prop::decode_variable(col.prop_type, &bytes)?
            } else {
                // In a TC, fixed values (including 8-byte) are stored inline.
                prop::decode_fixed(col.prop_type, cell)?
            };
            props.insert(
                col.prop_id,
                Property {
                    type_: col.prop_type,
                    value,
                },
            );
        }
        Ok(PropertyContext { props })
    }
}

struct ColDesc {
    prop_id: u16,
    prop_type: u16,
    ib_data: usize,
    cb_data: usize,
    i_bit: usize,
}

/// A single decoded property: its type tag and value.
#[derive(Clone, Debug)]
pub struct Property {
    pub type_: u16,
    pub value: Value,
}

/// A set of properties keyed by property ID.
#[derive(Clone, Debug, Default)]
pub struct PropertyContext {
    pub props: HashMap<u16, Property>,
}

impl PropertyContext {
    /// Get a raw property value by ID.
    pub fn get(&self, id: u16) -> Option<&Value> {
        self.props.get(&id).map(|p| &p.value)
    }

    /// All property IDs present, in arbitrary order.
    pub fn keys(&self) -> Vec<u16> {
        self.props.keys().copied().collect()
    }

    /// Get a string property, if present and string-typed.
    pub fn string(&self, id: u16) -> Option<String> {
        match self.get(id) {
            Some(Value::String(s)) => Some(s.clone()),
            _ => None,
        }
    }

    /// Get an i32 property (accepts I16/I32).
    pub fn i32(&self, id: u16) -> Option<i32> {
        match self.get(id) {
            Some(Value::I32(v)) => Some(*v),
            Some(Value::I16(v)) => Some(*v as i32),
            _ => None,
        }
    }

    /// Get an i64 property (accepts I16/I32/I64).
    pub fn i64(&self, id: u16) -> Option<i64> {
        match self.get(id) {
            Some(Value::I64(v)) => Some(*v),
            Some(Value::I32(v)) => Some(*v as i64),
            Some(Value::I16(v)) => Some(*v as i64),
            _ => None,
        }
    }

    /// Get a boolean property.
    pub fn bool(&self, id: u16) -> Option<bool> {
        match self.get(id) {
            Some(Value::Bool(b)) => Some(*b),
            _ => None,
        }
    }

    /// Get a FILETIME property as raw 100-ns ticks since 1601.
    pub fn time(&self, id: u16) -> Option<u64> {
        match self.get(id) {
            Some(Value::Time(t)) => Some(*t),
            _ => None,
        }
    }

    /// Get a binary property's bytes.
    pub fn binary(&self, id: u16) -> Option<Vec<u8>> {
        match self.get(id) {
            Some(Value::Binary(b)) => Some(b.clone()),
            _ => None,
        }
    }
}

/// A decoded table: rows of (rowId, properties).
pub struct TableContext {
    pub rows: Vec<(u32, PropertyContext)>,
}
