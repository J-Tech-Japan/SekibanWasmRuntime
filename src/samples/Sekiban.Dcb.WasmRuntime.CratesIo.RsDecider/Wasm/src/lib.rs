use crates_io_rs_decider_domain::{WeatherForecastDomain, WeatherForecastMvV1};

sekiban_wasm::export_domain!(WeatherForecastDomain);

sekiban_mv::export_mv!(vec![std::sync::Arc::new(WeatherForecastMvV1)
    as std::sync::Arc<dyn sekiban_mv::projector::WasmMvProjector>,]);
