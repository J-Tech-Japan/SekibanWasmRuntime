use std::alloc::{alloc, dealloc, Layout};

#[inline]
pub fn wasm_alloc(size: usize) -> *mut u8 {
    if size == 0 {
        return std::ptr::null_mut();
    }

    let layout = Layout::from_size_align(size, 1).expect("invalid layout");
    unsafe { alloc(layout) }
}

#[inline]
pub fn wasm_dealloc(ptr: *mut u8, size: usize) {
    if ptr.is_null() || size == 0 {
        return;
    }

    let layout = Layout::from_size_align(size, 1).expect("invalid layout");
    unsafe { dealloc(ptr, layout) }
}

#[inline]
pub fn pack_ptr_len(ptr: i32, len: i32) -> i64 {
    ((ptr as i64) << 32) | (len as i64 & 0xFFFF_FFFF)
}

#[inline]
pub fn unpack_ptr_len(packed: i64) -> (i32, i32) {
    let ptr = (packed >> 32) as i32;
    let len = (packed & 0xFFFF_FFFF) as i32;
    (ptr, len)
}
