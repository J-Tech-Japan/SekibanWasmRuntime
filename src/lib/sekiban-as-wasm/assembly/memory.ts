// Pinned-buffer memory management for Sekiban AssemblyScript projector modules.
//
// The Sekiban WASM Runtime host allocates guest memory through the exported
// `alloc`/`dealloc` pair and exchanges strings as (ptr << 32 | byteLength)
// packed u64 values produced by `writeStr`.
//
// Buffers are kept alive purely by `__pin`; the host releases them by calling
// `dealloc`, which unpins. No SDK-side bookkeeping is retained, so long-running
// projector modules do not accumulate per-call state.

export function alloc(size: u32): u32 {
  const ptr = __new(size as i32, 0);
  __pin(ptr);
  return ptr as u32;
}

export function dealloc(ptr: u32, size: u32): void {
  __unpin(ptr as usize);
}

export function readStr(ptr: u32, len: u32): string {
  return String.UTF8.decodeUnsafe(ptr as usize, len as i32);
}

export function writeStr(value: string): u64 {
  const buf = String.UTF8.encode(value);
  const p = changetype<usize>(buf);
  __pin(p);
  const byteLen = buf.byteLength;
  return (u64(p) << 32) | u64(byteLen);
}
