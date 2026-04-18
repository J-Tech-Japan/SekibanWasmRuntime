use sekiban_dcb_decider_rust_eventsource::DeciderDomain;
use sekiban_dcb_decider_rust_eventsource::materialized_view::ClassRoomEnrollmentMvV1;

sekiban_wasm::export_domain!(DeciderDomain);

sekiban_mv::export_mv!(vec![
    std::sync::Arc::new(ClassRoomEnrollmentMvV1) as std::sync::Arc<dyn sekiban_mv::projector::WasmMvProjector>,
]);
