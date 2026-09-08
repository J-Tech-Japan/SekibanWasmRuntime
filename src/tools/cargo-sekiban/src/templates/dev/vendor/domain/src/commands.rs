use async_trait::async_trait;
use chrono::Utc;
use sekiban_core::prelude::*;
use sekiban_derive::Command;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::events::*;
use crate::states::*;
use crate::tags::*;

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateWeatherForecast {
    pub forecast_id: Option<Uuid>,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
}

#[async_trait]
impl CommandHandler for CreateWeatherForecast {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        ctx: &C,
    ) -> Result<Option<CommandOutput>, CommandError> {
        let forecast_id = self.forecast_id.unwrap_or_else(Uuid::now_v7);
        let tag = WeatherForecastTag::new(forecast_id);
        let (state, _version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;
        if !state.is_empty() {
            return Err(CommandError::AlreadyExists(forecast_id.to_string()));
        }

        let event = WeatherForecastCreated {
            forecast_id,
            location: self.location.clone(),
            temperature_c: self.temperature_c,
            summary: self.summary.clone(),
            created_at: Utc::now().to_rfc3339(),
        };

        Ok(Some(CommandOutput::single(event, tag).map_err(|err| {
            CommandError::Serialization(err.to_string())
        })?))
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct UpdateWeatherForecastLocation {
    pub forecast_id: Uuid,
    pub new_location: String,
}

#[async_trait]
impl CommandHandler for UpdateWeatherForecastLocation {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        ctx: &C,
    ) -> Result<Option<CommandOutput>, CommandError> {
        let tag = WeatherForecastTag::new(self.forecast_id);
        let (state, version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;

        if state.is_empty() {
            return Err(CommandError::NotFound(self.forecast_id.to_string()));
        }
        if state.is_deleted {
            return Err(CommandError::Deleted(self.forecast_id.to_string()));
        }

        let event = WeatherForecastLocationUpdated {
            forecast_id: self.forecast_id,
            new_location: self.new_location.clone(),
            updated_at: Utc::now().to_rfc3339(),
        };

        let output = CommandOutput::single(event, tag.clone())
            .map_err(|err| CommandError::Serialization(err.to_string()))?
            .with_expected_version(tag, version);
        Ok(Some(output))
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct DeleteWeatherForecast {
    pub forecast_id: Uuid,
}

#[async_trait]
impl CommandHandler for DeleteWeatherForecast {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        ctx: &C,
    ) -> Result<Option<CommandOutput>, CommandError> {
        let tag = WeatherForecastTag::new(self.forecast_id);
        let (state, version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;

        if state.is_empty() {
            return Err(CommandError::NotFound(self.forecast_id.to_string()));
        }
        if state.is_deleted {
            return Ok(None);
        }

        let event = WeatherForecastDeleted {
            forecast_id: self.forecast_id,
            deleted_at: Utc::now().to_rfc3339(),
        };

        let output = CommandOutput::single(event, tag.clone())
            .map_err(|err| CommandError::Serialization(err.to_string()))?
            .with_expected_version(tag, version);
        Ok(Some(output))
    }
}
