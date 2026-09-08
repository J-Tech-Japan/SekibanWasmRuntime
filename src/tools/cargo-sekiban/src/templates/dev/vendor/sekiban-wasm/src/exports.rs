/// Export a domain as WASM C-ABI functions.
#[macro_export]
macro_rules! export_domain {
    ($domain:ty) => {
        #[no_mangle]
        pub extern "C" fn alloc(size: i32) -> i32 {
            $crate::memory::wasm_alloc(size as usize) as i32
        }

        #[no_mangle]
        pub extern "C" fn dealloc(ptr: i32, len: i32) {
            $crate::memory::wasm_dealloc(ptr as *mut u8, len as usize)
        }

        #[no_mangle]
        pub extern "C" fn create_instance(name_ptr: i32, name_len: i32) -> i32 {
            let name = unsafe { $crate::ffi::read_string(name_ptr, name_len) };
            $crate::instance::create_instance_by_name::<$domain>(&name)
        }

        #[no_mangle]
        pub extern "C" fn apply_event(
            instance_id: i32,
            event_type_ptr: i32,
            event_type_len: i32,
            payload_ptr: i32,
            payload_len: i32,
        ) {
            let event_type = unsafe { $crate::ffi::read_string(event_type_ptr, event_type_len) };
            let payload = unsafe { $crate::ffi::read_string(payload_ptr, payload_len) };

            $crate::instance::apply_event_to_instance(instance_id, &event_type, &payload);
        }

        #[no_mangle]
        pub extern "C" fn apply_events_batch(instance_id: i32, json_ptr: i32, json_len: i32) -> i32 {
            let json = unsafe { $crate::ffi::read_string(json_ptr, json_len) };
            if json.trim().is_empty() {
                return 0;
            }

            let mut applied = 0_i32;
            let parsed: Result<$crate::serde_json::Value, _> = $crate::serde_json::from_str(&json);
            let v = match parsed {
                Ok(v) => v,
                Err(_) => return -1,
            };
            let Some(arr) = v.as_array() else {
                return -1;
            };

            for item in arr {
                let Some(obj) = item.as_object() else {
                    break;
                };
                let Some(event_type) = obj.get("eventType").and_then(|x| x.as_str()) else {
                    break;
                };
                let Some(payload_json) = obj.get("payloadJson").and_then(|x| x.as_str()) else {
                    break;
                };
                if event_type.trim().is_empty() {
                    break;
                }
                $crate::instance::apply_event_to_instance(instance_id, event_type, payload_json);
                applied += 1;
            }
            applied
        }

        #[no_mangle]
        pub extern "C" fn serialize_state(instance_id: i32) -> i64 {
            let state = $crate::instance::get_instance_state(instance_id);
            $crate::ffi::write_string(&state)
        }

        #[no_mangle]
        pub extern "C" fn restore_state(
            instance_id: i32,
            state_ptr: i32,
            state_len: i32,
        ) {
            let state_json = unsafe { $crate::ffi::read_string(state_ptr, state_len) };
            $crate::instance::restore_instance_state(instance_id, &state_json);
        }

        #[no_mangle]
        pub extern "C" fn execute_query(
            instance_id: i32,
            query_type_ptr: i32,
            query_type_len: i32,
            params_ptr: i32,
            params_len: i32,
        ) -> i64 {
            let query_type = unsafe { $crate::ffi::read_string(query_type_ptr, query_type_len) };
            let params = unsafe { $crate::ffi::read_string(params_ptr, params_len) };
            let result = $crate::instance::execute_query(instance_id, &query_type, &params);
            $crate::ffi::write_string(&result)
        }

        #[no_mangle]
        pub extern "C" fn execute_list_query(
            instance_id: i32,
            query_type_ptr: i32,
            query_type_len: i32,
            params_ptr: i32,
            params_len: i32,
        ) -> i64 {
            let query_type = unsafe { $crate::ffi::read_string(query_type_ptr, query_type_len) };
            let params = unsafe { $crate::ffi::read_string(params_ptr, params_len) };
            let result = $crate::instance::execute_list_query(instance_id, &query_type, &params);
            $crate::ffi::write_string(&result)
        }
    };
}
