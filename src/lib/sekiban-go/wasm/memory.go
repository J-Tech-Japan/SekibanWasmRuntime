package wasm

import "unsafe"

// Allocations keeps buffers alive to prevent GC in TinyGo reactor mode.
var Allocations [][]byte

// Alloc allocates a buffer in WASM linear memory and returns its pointer.
func Alloc(size uint32) uint32 {
	if size == 0 {
		return 0
	}
	buf := make([]byte, int(size))
	ptr := uintptr(unsafe.Pointer(&buf[0]))
	Allocations = append(Allocations, buf)
	return uint32(ptr)
}

// Dealloc is a no-op for TinyGo reactor mode to avoid traps.
func Dealloc(ptr uint32, length uint32) {
	// No-op: TinyGo WASI reactor mode is sensitive to allocator bookkeeping.
}

// ReadString reads a UTF-8 string from WASM linear memory.
func ReadString(ptr uint32, length uint32) string {
	if ptr == 0 || length == 0 {
		return ""
	}
	data := unsafe.Slice((*byte)(unsafe.Pointer(uintptr(ptr))), int(length))
	return string(data)
}

// WriteString writes a string to WASM linear memory and returns packed ptr|len.
func WriteString(value string) int64 {
	if value == "" {
		return PackPtrLen(0, 0)
	}
	bytes := []byte(value)
	ptr := Alloc(uint32(len(bytes)))
	if ptr == 0 {
		return PackPtrLen(0, 0)
	}
	dest := unsafe.Slice((*byte)(unsafe.Pointer(uintptr(ptr))), len(bytes))
	copy(dest, bytes)
	return PackPtrLen(ptr, uint32(len(bytes)))
}

// PackPtrLen packs a pointer and length into a single int64 value.
func PackPtrLen(ptr uint32, length uint32) int64 {
	return int64(uint64(ptr)<<32 | uint64(length))
}
