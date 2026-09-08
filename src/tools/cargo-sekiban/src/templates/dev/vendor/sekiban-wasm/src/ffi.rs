use std::slice;
use std::str;

/// Read a string from WASM memory.
///
/// # Safety
/// ptr/len must be valid memory.
pub unsafe fn read_string(ptr: i32, len: i32) -> String {
    if ptr == 0 || len <= 0 {
        return String::new();
    }

    let slice = slice::from_raw_parts(ptr as *const u8, len as usize);
    str::from_utf8(slice)
        .map(|s| s.to_string())
        .unwrap_or_default()
}

/// Write a string into WASM memory, returning packed ptr/len.
pub fn write_string(s: &str) -> i64 {
    let bytes = s.as_bytes();
    let len = bytes.len();
    if len == 0 {
        return crate::memory::pack_ptr_len(0, 0);
    }

    let ptr = crate::memory::wasm_alloc(len);
    if ptr.is_null() {
        return crate::memory::pack_ptr_len(0, 0);
    }

    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), ptr, len);
    }

    crate::memory::pack_ptr_len(ptr as i32, len as i32)
}

/// Serialize to JSON and write to WASM memory.
pub fn write_json<T: serde::Serialize>(value: &T) -> i64 {
    match serde_json::to_string(value) {
        Ok(json) => write_string(&json),
        Err(_) => write_string("{}"),
    }
}

/// Write a JSON error response.
pub fn write_error(error: &str) -> i64 {
    let response = serde_json::json!({ "error": error });
    write_json(&response)
}
