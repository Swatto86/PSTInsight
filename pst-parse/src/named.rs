//! Named property map (the name-to-id map, NID 0x61).
//!
//! Resolves property IDs >= 0x8000 to their (GUID, name/LID) identity. See
//! [MS-PST] section 2.4.7.

use std::collections::HashMap;

use crate::error::Result;
use crate::ltp::PropertyContext;
use crate::prop;
use crate::util;

/// The GUID of a named property's property set.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum PropSet {
    /// PS_MAPI ({00020328-...}).
    Mapi,
    /// PS_PUBLIC_STRINGS ({00020329-...}).
    PublicStrings,
    /// An explicit GUID from the map's GUID stream.
    Guid([u8; 16]),
    /// No / invalid GUID index.
    None,
}

/// The identity a named property resolves to.
#[derive(Clone, Debug)]
pub enum NamedId {
    /// A numeric long ID (LID) within a property set.
    Lid(u32),
    /// A string name within a property set.
    Name(String),
}

/// A resolved named property.
#[derive(Clone, Debug)]
pub struct NamedProp {
    pub set: PropSet,
    pub id: NamedId,
}

/// Maps local property IDs (0x8000+) to their named-property identities.
#[derive(Clone, Debug, Default)]
pub struct NamedPropMap {
    map: HashMap<u16, NamedProp>,
}

impl NamedPropMap {
    /// An empty map (used when a file has no name-to-id node).
    pub fn empty() -> Self {
        NamedPropMap::default()
    }

    /// Resolve a property ID to its named-property identity, if it is named.
    pub fn get(&self, prop_id: u16) -> Option<&NamedProp> {
        self.map.get(&prop_id)
    }

    /// Number of named properties.
    pub fn len(&self) -> usize {
        self.map.len()
    }

    /// Whether the map is empty.
    pub fn is_empty(&self) -> bool {
        self.map.is_empty()
    }

    /// Build the map from the property context of node 0x61.
    pub(crate) fn from_pc(pc: &PropertyContext) -> Result<Self> {
        let guid_stream = pc.binary(prop::PID_NAMEID_STREAM_GUID).unwrap_or_default();
        let entry_stream = pc.binary(prop::PID_NAMEID_STREAM_ENTRY).unwrap_or_default();
        let string_stream = pc
            .binary(prop::PID_NAMEID_STREAM_STRING)
            .unwrap_or_default();

        let mut map = HashMap::new();
        // Entry stream is an array of 8-byte NAMEID structures.
        for entry in entry_stream.chunks_exact(8) {
            let property_id = u32::from_le_bytes([entry[0], entry[1], entry[2], entry[3]]);
            let flags = u16::from_le_bytes([entry[4], entry[5]]);
            let prop_idx = u16::from_le_bytes([entry[6], entry[7]]);
            let is_string = (flags & 0x1) == 1;
            let guid_index = flags >> 1;

            let set = match guid_index {
                0 => PropSet::None,
                1 => PropSet::Mapi,
                2 => PropSet::PublicStrings,
                n => {
                    let off = (n as usize - 3) * 16;
                    match util::bytes(&guid_stream, off, 16) {
                        Ok(g) => {
                            let mut buf = [0u8; 16];
                            buf.copy_from_slice(g);
                            PropSet::Guid(buf)
                        }
                        Err(_) => PropSet::None,
                    }
                }
            };

            let id = if is_string {
                // property_id is a byte offset into the string stream:
                // u32 length, then that many bytes of UTF-16LE.
                let off = property_id as usize;
                let name = match util::u32(&string_stream, off) {
                    Ok(len) => {
                        let len = len as usize;
                        match util::bytes(&string_stream, off + 4, len) {
                            Ok(b) => decode_utf16(b),
                            Err(_) => String::new(),
                        }
                    }
                    Err(_) => String::new(),
                };
                NamedId::Name(name)
            } else {
                NamedId::Lid(property_id)
            };

            map.insert(0x8000 + prop_idx, NamedProp { set, id });
        }
        Ok(NamedPropMap { map })
    }
}

fn decode_utf16(b: &[u8]) -> String {
    let units: Vec<u16> = b
        .chunks_exact(2)
        .map(|c| u16::from_le_bytes([c[0], c[1]]))
        .collect();
    String::from_utf16_lossy(&units)
}
