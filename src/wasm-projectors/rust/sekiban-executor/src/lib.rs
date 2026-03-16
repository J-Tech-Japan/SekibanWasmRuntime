use std::{collections::HashMap, collections::HashSet, sync::Arc, time::Duration};

use anyhow::{anyhow, Context};
use async_trait::async_trait;
use base64::{engine::general_purpose::STANDARD, Engine as _};
use reqwest::Client;
use sekiban_core::{CommandContext, CommandError, CommandMeta, CommandOutput};
use sekiban_core::{ListQuery, Query, StatePayload, Tag};
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use serde_json::Value;
use thiserror::Error;

pub trait TagStateProjectorResolver: Send + Sync {
    fn resolve_projector_name(&self, tag_group: &str) -> Option<String>;
}

#[derive(Clone, Debug, Default)]
pub struct StaticTagProjectorResolver {
    mappings: HashMap<String, String>,
    default_projector: Option<String>,
}

impl StaticTagProjectorResolver {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn with_tag_group(
        mut self,
        tag_group: impl Into<String>,
        projector_name: impl Into<String>,
    ) -> Self {
        self.mappings
            .insert(tag_group.into(), projector_name.into());
        self
    }

    pub fn with_default_projector(mut self, projector_name: impl Into<String>) -> Self {
        self.default_projector = Some(projector_name.into());
        self
    }
}

impl TagStateProjectorResolver for StaticTagProjectorResolver {
    fn resolve_projector_name(&self, tag_group: &str) -> Option<String> {
        self.mappings
            .get(tag_group)
            .cloned()
            .or_else(|| self.default_projector.clone())
    }
}

#[derive(Clone, Debug)]
pub struct HttpSekibanExecutorOptions {
    pub max_attempts: usize,
    pub retry_delay: Duration,
}

impl Default for HttpSekibanExecutorOptions {
    fn default() -> Self {
        Self {
            max_attempts: 5,
            retry_delay: Duration::from_millis(250),
        }
    }
}

#[derive(Clone)]
struct HttpSekibanTransport {
    base_url: String,
    client: Client,
    options: HttpSekibanExecutorOptions,
}

impl HttpSekibanTransport {
    fn new(
        base_url: impl Into<String>,
        client: Client,
        options: HttpSekibanExecutorOptions,
    ) -> Self {
        Self {
            base_url: base_url.into().trim_end_matches('/').to_string(),
            client,
            options,
        }
    }

    async fn post_with_retry<T: Serialize>(
        &self,
        relative_path: &str,
        body: &T,
    ) -> Result<reqwest::Response, reqwest::Error> {
        let url = format!("{}{}", self.base_url, relative_path);
        let mut last_err: Option<reqwest::Error> = None;

        for attempt in 1..=self.options.max_attempts {
            match self.client.post(&url).json(body).send().await {
                Ok(response) => return Ok(response),
                Err(err) => {
                    last_err = Some(err);
                    if attempt < self.options.max_attempts {
                        tokio::time::sleep(self.options.retry_delay).await;
                    }
                }
            }
        }

        Err(last_err.expect("retry loop should capture at least one error"))
    }
}

#[derive(Clone)]
pub struct HttpCommandContext {
    transport: HttpSekibanTransport,
    resolver: Arc<dyn TagStateProjectorResolver>,
}

impl HttpCommandContext {
    fn new(transport: HttpSekibanTransport, resolver: Arc<dyn TagStateProjectorResolver>) -> Self {
        Self {
            transport,
            resolver,
        }
    }

    pub async fn get_tag_state(
        &self,
        tag: &str,
    ) -> Result<SerializableTagStateResponse, CommandError> {
        let (tag_group, tag_id) = parse_tag(tag)
            .ok_or_else(|| CommandError::Validation(format!("invalid tag format: {tag}")))?;
        let projector_name = self
            .resolver
            .resolve_projector_name(tag_group)
            .ok_or_else(|| {
                CommandError::Validation(format!(
                    "tag group '{tag_group}' is not mapped to a projector"
                ))
            })?;

        let request = TagStateRequest {
            tag_state_id: format!("{tag_group}:{tag_id}:{projector_name}"),
        };

        let response = self
            .transport
            .post_with_retry("/api/sekiban/serialized/tag-state", &request)
            .await
            .map_err(|err| CommandError::Validation(format!("tag-state request failed: {err}")))?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response
                .text()
                .await
                .unwrap_or_else(|err| format!("<failed to read error body: {err}>"));
            return Err(CommandError::Validation(format!(
                "tag-state request failed with status {}: {}",
                status, body
            )));
        }

        response
            .json::<SerializableTagStateResponse>()
            .await
            .map_err(|err| {
                CommandError::Serialization(format!("invalid tag-state response: {err}"))
            })
    }
}

#[async_trait]
impl CommandContext for HttpCommandContext {
    async fn get_state<S: StatePayload, T: Tag>(&self, tag: &T) -> Result<(S, i32), CommandError> {
        let response = self.get_tag_state(&tag.to_tag_string()).await?;
        if response.payload.is_empty() {
            return Ok((S::default(), response.version));
        }

        let bytes = STANDARD
            .decode(response.payload)
            .map_err(|err| CommandError::Serialization(format!("invalid state payload: {err}")))?;
        if bytes.is_empty() {
            return Ok((S::default(), response.version));
        }

        let state = serde_json::from_slice::<S>(&bytes)
            .map_err(|err| CommandError::Serialization(format!("invalid state json: {err}")))?;
        Ok((state, response.version))
    }

    async fn tag_exists<T: Tag>(&self, tag: &T) -> Result<bool, CommandError> {
        let response = self.get_tag_state(&tag.to_tag_string()).await?;
        Ok(!response.payload.is_empty())
    }
}

#[async_trait]
pub trait SekibanCommandCommitRequestBuilder: Send + Sync {
    async fn build_commit_request(
        &self,
        context: &HttpCommandContext,
        command_name: &str,
        command_payload: &Value,
    ) -> Result<Option<CommitRequest>, CommandError>;
}

#[derive(Debug, Error)]
pub enum ExecuteCommandError {
    #[error(transparent)]
    Build(#[from] CommandError),

    #[error(transparent)]
    Transport(#[from] anyhow::Error),
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CommandExecutionResult {
    pub status_code: u16,
    pub success: bool,
    pub sortable_unique_id: Option<String>,
    pub response_body: Value,
}

#[async_trait]
pub trait SekibanExecutor: Send + Sync {
    async fn execute_command_value(
        &self,
        command_name: &str,
        command_payload: Value,
    ) -> Result<CommandExecutionResult, ExecuteCommandError>;

    async fn execute_query_value(&self, request: SerializedQueryRequest) -> anyhow::Result<Value>;

    async fn execute_list_query_value(
        &self,
        request: SerializedQueryRequest,
    ) -> anyhow::Result<Value>;
}

#[derive(Clone)]
pub struct HttpSekibanExecutor<B> {
    transport: HttpSekibanTransport,
    builder: Arc<B>,
    resolver: Arc<dyn TagStateProjectorResolver>,
}

impl<B> HttpSekibanExecutor<B>
where
    B: SekibanCommandCommitRequestBuilder + Send + Sync + 'static,
{
    pub fn new(
        base_url: impl Into<String>,
        builder: B,
        resolver: impl TagStateProjectorResolver + 'static,
    ) -> Self {
        Self::with_client_and_options(
            base_url,
            Client::new(),
            builder,
            resolver,
            HttpSekibanExecutorOptions::default(),
        )
    }

    pub fn with_client(
        base_url: impl Into<String>,
        client: Client,
        builder: B,
        resolver: impl TagStateProjectorResolver + 'static,
    ) -> Self {
        Self::with_client_and_options(
            base_url,
            client,
            builder,
            resolver,
            HttpSekibanExecutorOptions::default(),
        )
    }

    pub fn with_client_and_options(
        base_url: impl Into<String>,
        client: Client,
        builder: B,
        resolver: impl TagStateProjectorResolver + 'static,
        options: HttpSekibanExecutorOptions,
    ) -> Self {
        Self {
            transport: HttpSekibanTransport::new(base_url, client, options),
            builder: Arc::new(builder),
            resolver: Arc::new(resolver),
        }
    }

    pub fn command_context(&self) -> HttpCommandContext {
        HttpCommandContext::new(self.transport.clone(), self.resolver.clone())
    }

    pub async fn execute_command<T>(
        &self,
        command: &T,
    ) -> Result<CommandExecutionResult, ExecuteCommandError>
    where
        T: CommandMeta + Serialize + Send + Sync,
    {
        self.execute_named_command(T::COMMAND_TYPE, command).await
    }

    pub async fn execute_named_command<T>(
        &self,
        command_name: &str,
        command: &T,
    ) -> Result<CommandExecutionResult, ExecuteCommandError>
    where
        T: Serialize + Send + Sync,
    {
        let payload = serde_json::to_value(command)
            .map_err(|err| ExecuteCommandError::Transport(anyhow!(err)))?;
        self.execute_command_value(command_name, payload).await
    }

    pub async fn execute_query<Q, R>(&self, query: &Q) -> anyhow::Result<R>
    where
        Q: Query + Serialize + Send + Sync,
        R: DeserializeOwned,
    {
        let request = SerializedQueryRequest {
            query_type: Q::QUERY_TYPE.to_string(),
            query_params_json: serde_json::to_string(query)?,
            wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
        };

        let value = self.execute_query_value(request).await?;
        Ok(serde_json::from_value(value)?)
    }

    pub async fn execute_list_query<Q, R>(&self, query: &Q) -> anyhow::Result<R>
    where
        Q: ListQuery + Serialize + Send + Sync,
        R: DeserializeOwned,
    {
        let request = SerializedQueryRequest {
            query_type: Q::QUERY_TYPE.to_string(),
            query_params_json: serde_json::to_string(query)?,
            wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
        };

        let value = self.execute_list_query_value(request).await?;
        Ok(serde_json::from_value(value)?)
    }
}

#[async_trait]
impl<B> SekibanExecutor for HttpSekibanExecutor<B>
where
    B: SekibanCommandCommitRequestBuilder + Send + Sync + 'static,
{
    async fn execute_command_value(
        &self,
        command_name: &str,
        command_payload: Value,
    ) -> Result<CommandExecutionResult, ExecuteCommandError> {
        let context = self.command_context();
        let commit_request = self
            .builder
            .build_commit_request(&context, command_name, &command_payload)
            .await?;

        let Some(commit_request) = commit_request else {
            return Ok(CommandExecutionResult {
                status_code: 200,
                success: true,
                sortable_unique_id: None,
                response_body: serde_json::json!({ "success": true }),
            });
        };

        let response = self
            .transport
            .post_with_retry("/api/sekiban/serialized/commit", &commit_request)
            .await
            .context("commit request failed")?;

        let status = response.status();
        let response_body: Value = response
            .json()
            .await
            .unwrap_or_else(|_| serde_json::json!({ "error": "failed to read commit response" }));

        Ok(CommandExecutionResult {
            status_code: status.as_u16(),
            success: status.is_success(),
            sortable_unique_id: extract_sortable_unique_id(&response_body),
            response_body,
        })
    }

    async fn execute_query_value(&self, request: SerializedQueryRequest) -> anyhow::Result<Value> {
        let response = self
            .transport
            .post_with_retry("/api/sekiban/serialized/query", &request)
            .await
            .context("serialized query request failed")?;
        let response = response.error_for_status()?;
        let body: SerializedQueryResponse = response.json().await?;
        Ok(serde_json::from_str(&body.result_json)?)
    }

    async fn execute_list_query_value(
        &self,
        request: SerializedQueryRequest,
    ) -> anyhow::Result<Value> {
        let response = self
            .transport
            .post_with_retry("/api/sekiban/serialized/list-query", &request)
            .await
            .context("serialized list query request failed")?;
        let response = response.error_for_status()?;
        let body: SerializedListQueryResponse = response.json().await?;
        Ok(serde_json::from_str(&body.items_json)?)
    }
}

pub async fn build_commit_request_from_output(
    context: &HttpCommandContext,
    output: CommandOutput,
) -> Result<CommitRequest, CommandError> {
    let event_candidates = output
        .events
        .into_iter()
        .map(|event| CommitEventCandidate {
            payload: STANDARD.encode(event.payload.as_bytes()),
            event_payload_name: event.event_type,
            tags: output.tags.clone(),
        })
        .collect();

    let mut seen = HashSet::new();
    let mut consistency_tags = Vec::new();
    for tag in output.consistency_tags {
        if !seen.insert(tag.clone()) {
            continue;
        }

        let state = context.get_tag_state(&tag).await?;
        consistency_tags.push(ConsistencyTag {
            tag,
            last_sortable_unique_id: state.last_sorted_unique_id,
        });
    }

    Ok(CommitRequest {
        event_candidates,
        consistency_tags,
    })
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TagStateRequest {
    pub tag_state_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SerializableTagStateResponse {
    pub payload: String,
    pub version: i32,
    pub last_sorted_unique_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SerializedQueryRequest {
    pub query_type: String,
    pub query_params_json: String,
    pub wait_for_sortable_unique_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SerializedQueryResponse {
    pub result_json: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SerializedListQueryResponse {
    pub items_json: String,
    pub total_count: Option<i32>,
    pub total_pages: Option<i32>,
    pub current_page: Option<i32>,
    pub page_size: Option<i32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CommitRequest {
    pub event_candidates: Vec<CommitEventCandidate>,
    pub consistency_tags: Vec<ConsistencyTag>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CommitEventCandidate {
    pub payload: String,
    pub event_payload_name: String,
    pub tags: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ConsistencyTag {
    pub tag: String,
    pub last_sortable_unique_id: String,
}

pub fn extract_sortable_unique_id(body: &Value) -> Option<String> {
    body.get("sortableUniqueId")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
        .or_else(|| {
            body.get("writtenEvents")
                .and_then(Value::as_array)
                .and_then(|events| events.first())
                .and_then(|event| event.get("sortableUniqueIdValue"))
                .and_then(Value::as_str)
                .map(ToOwned::to_owned)
        })
}

fn parse_tag(tag: &str) -> Option<(&str, &str)> {
    let (group, content) = tag.split_once(':')?;
    if group.is_empty() || content.is_empty() {
        return None;
    }
    Some((group, content))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn static_resolver_prefers_explicit_tag_mapping() {
        let resolver = StaticTagProjectorResolver::new()
            .with_default_projector("FallbackProjector")
            .with_tag_group("weather", "WeatherForecastProjector");

        assert_eq!(
            resolver.resolve_projector_name("weather").as_deref(),
            Some("WeatherForecastProjector")
        );
        assert_eq!(
            resolver.resolve_projector_name("unknown").as_deref(),
            Some("FallbackProjector")
        );
    }

    #[test]
    fn extract_sortable_unique_id_reads_top_level_and_written_events() {
        let top_level = serde_json::json!({ "sortableUniqueId": "sort-1" });
        assert_eq!(
            extract_sortable_unique_id(&top_level).as_deref(),
            Some("sort-1")
        );

        let nested = serde_json::json!({
            "writtenEvents": [
                { "sortableUniqueIdValue": "sort-2" }
            ]
        });
        assert_eq!(
            extract_sortable_unique_id(&nested).as_deref(),
            Some("sort-2")
        );
    }
}
