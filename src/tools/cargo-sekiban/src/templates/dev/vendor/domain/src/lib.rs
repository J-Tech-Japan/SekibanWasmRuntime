pub mod commands;
pub mod events;
pub mod materialized_view;
pub mod projectors;
pub mod queries;
pub mod states;
pub mod tags;

pub use commands::*;
pub use events::*;
pub use materialized_view::*;
pub use projectors::*;
pub use queries::*;
pub use states::*;
pub use tags::*;

use sekiban_core::prelude::*;

domain_types!(WeatherForecastDomain {
    events: [
        WeatherForecastCreated,
        WeatherForecastLocationUpdated,
        WeatherForecastDeleted,
    ],
    tags: [WeatherForecastTag,],
    tag_projectors: [WeatherForecastProjector,],
    multi_projectors: [WeatherForecastListProjector,],
    commands: [
        CreateWeatherForecast,
        UpdateWeatherForecastLocation,
        DeleteWeatherForecast,
    ],
    queries: [GetWeatherForecastCountQuery,],
    list_queries: [GetWeatherForecastListQuery,],
});
