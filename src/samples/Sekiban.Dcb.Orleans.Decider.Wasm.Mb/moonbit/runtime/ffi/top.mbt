pub extern "wasm" fn store8(offset : Int, value : Int) =
#|(func (param i32) (param i32) local.get 0 local.get 1 i32.store8)

pub extern "wasm" fn load8_u(offset : Int) -> Int =
#|(func (param i32) (result i32) local.get 0 i32.load8_u)

pub extern "wasm" fn malloc(size : Int) -> Int =
#|(func (param i32) (result i32) (local i32)
#| local.get 0 i32.const 4 i32.add call $moonbit.gc.malloc
#| local.tee 1 i32.const 0 call $moonbit.init_array8
#| local.get 1 i32.const 8 i32.add)

pub extern "wasm" fn free(position : Int) =
#|(func (param i32) local.get 0 i32.const 8 i32.sub call $moonbit.decref)

pub fn ptr2str(ptr : Int, len : Int) -> String {
  if ptr == 0 || len <= 0 {
    return ""
  }
  let bytes = Bytes::makei(len, i => load8_u(ptr + i).to_byte())
  @encoding/utf8.decode_lossy(bytes, ignore_bom=true)
}
