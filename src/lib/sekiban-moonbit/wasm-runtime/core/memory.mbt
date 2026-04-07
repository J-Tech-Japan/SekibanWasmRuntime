pub fn pack_ptr_len(ptr : Int, len : Int) -> Int64 {
  ((ptr.to_int64()) << 32) | (len.to_int64())
}

pub fn read_utf8(ptr : Int, len : Int) -> String {
  @ffi.ptr2str(ptr, len)
}

pub fn write_utf8(s : String) -> Int64 {
  if s == "" {
    return pack_ptr_len(0, 0)
  }
  let bytes = @encoding/utf8.encode(s[:])
  let len = bytes.length()
  if len <= 0 {
    return pack_ptr_len(0, 0)
  }
  let ptr = @ffi.malloc(len)
  for i in 0..<len {
    @ffi.store8(ptr + i, bytes[i].to_int())
  }
  pack_ptr_len(ptr, len)
}

pub fn alloc(size : UInt) -> UInt {
  if size == 0 {
    return 0
  }
  (@ffi.malloc(size.reinterpret_as_int())).reinterpret_as_uint()
}

pub fn dealloc(ptr : UInt, _len : UInt) -> Unit {
  if ptr == 0 {
    return
  }
  @ffi.free(ptr.reinterpret_as_int())
}
