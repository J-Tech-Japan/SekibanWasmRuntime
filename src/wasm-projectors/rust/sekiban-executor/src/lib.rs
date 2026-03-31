use std::{collections::HashMap, collections::HashSet, sync::Arc, sync::Mutex, time::Duration};

use anyhow::{anyhow, Context};
use async_trait::async_trait;
use base64::{engine::general_purpose::STANDARD, Engine as _};
use reqwest::{Client, StatusCode};
use sekiban_core::{Command, CommandContext, CommandError, CommandMeta, CommandOutput};
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
    pub request_timeout: Duration,
    pub connect_timeout: Duration,
}

impl Default for HttpSekibanExecutorOptions {
    fn default() -> Self {
        Self {
            max_attempts: 5,
            retry_delay: Duration::from_millis(250),
            request_timeout: Duration::from_secs(30),
            connect_timeout: Duration::from_secs(5),
        }
    }
}

impl HttpSekibanExecutorOptions {
    fn normalized(self) -> Self {
        let defaults = Self::default();
        Self {
            max_attempts: self.max_attempts.max(1),
            retry_delay: self.retry_delay,
            request_timeout: if self.request_timeout.is_zero() {
                defaults.request_timeout
            } else {
                self.request_timeout
            },
            connect_timeout: if self.connect_timeout.is_zero() {
                defaults.connect_timeout
            } else {
                self.connect_timeout
            },
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
        let options = options.normalized();
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
    /// Cache of tag state responses to avoid redundant HTTP calls.
    /// Matches C#'s RemoteCommandContext._accessedTagStates pattern.
    tag_state_cache: Arc<Mutex<HashMap<String, SerializableTagStateResponse>>>,
}

pub type RemoteCommandContext = HttpCommandContext;

impl HttpCommandContext {
    fn new(transport: HttpSekibanTransport, resolver: Arc<dyn TagStateProjectorResolver>) -> Self {
        Self {
            transport,
            resolver,
            tag_state_cache: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub async fn get_tag_state(
        &self,
        tag: &str,
    ) -> Result<SerializableTagStateResponse, CommandError> {
        // Check cache first
        if let Some(cached) = self.tag_state_cache.lock().unwrap().get(tag) {
            return Ok(cached.clone());
        }

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

        let result = response
            .json::<SerializableTagStateResponse>()
            .await
            .map_err(|err| {
                CommandError::Serialization(format!("invalid tag-state response: {err}"))
            })?;

        // Cache the result
        self.tag_state_cache
            .lock()
            .unwrap()
            .insert(tag.to_string(), result.clone());

        Ok(result)
    }

    pub async fn get_tag_latest_sortable_unique_id(
        &self,
        tag: &str,
    ) -> Result<Option<String>, CommandError> {
        let request = TagLatestSortableRequest {
            tag: tag.to_string(),
        };

        let response = self
            .transport
            .post_with_retry("/api/sekiban/serialized/tag-latest-sortable", &request)
            .await
            .map_err(|err| {
                CommandError::Validation(format!("tag-latest-sortable request failed: {err}"))
            })?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response
                .text()
                .await
                .unwrap_or_else(|err| format!("<failed to read error body: {err}>"));
            return Err(CommandError::Validation(format!(
                "tag-latest-sortable request failed with status {}: {}",
                status, body
            )));
        }

        let body = response
            .json::<TagLatestSortableResponse>()
            .await
            .map_err(|err| {
                CommandError::Serialization(format!("invalid tag-latest-sortable response: {err}"))
            })?;

        Ok(body
            .exists
            .then_some(body.last_sortable_unique_id)
            .filter(|value| !value.is_empty()))
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
        Ok(self
            .get_tag_latest_sortable_unique_id(&tag.to_tag_string())
            .await?
            .is_some())
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

#[derive(Clone)]
pub struct RemoteSekibanExecutor {
    transport: HttpSekibanTransport,
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
        let options = HttpSekibanExecutorOptions::default();
        Self::with_client_and_options(
            base_url,
            build_default_http_client(&options),
            builder,
            resolver,
            options,
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
        let request = build_query_request::<Q>(query)?;

        let value = self.execute_query_value(request).await?;
        Ok(serde_json::from_value(value)?)
    }

    pub async fn execute_list_query<Q, R>(&self, query: &Q) -> anyhow::Result<R>
    where
        Q: ListQuery + Serialize + Send + Sync,
        R: DeserializeOwned,
    {
        let request = build_list_query_request::<Q>(query)?;

        let value = self.execute_list_query_value(request).await?;
        Ok(serde_json::from_value(value)?)
    }
}

impl RemoteSekibanExecutor {
    pub fn new(
        base_url: impl Into<String>,
        resolver: impl TagStateProjectorResolver + 'static,
    ) -> Self {
        let options = HttpSekibanExecutorOptions::default();
        Self::with_client_and_options(
            base_url,
            build_default_http_client(&options),
            resolver,
            options,
        )
    }

    pub fn with_client(
        base_url: impl Into<String>,
        client: Client,
        resolver: impl TagStateProjectorResolver + 'static,
    ) -> Self {
        Self::with_client_and_options(
            base_url,
            client,
            resolver,
            HttpSekibanExecutorOptions::default(),
        )
    }

    pub fn with_client_and_options(
        base_url: impl Into<String>,
        client: Client,
        resolver: impl TagStateProjectorResolver + 'static,
        options: HttpSekibanExecutorOptions,
    ) -> Self {
        Self {
            transport: HttpSekibanTransport::new(base_url, client, options),
            resolver: Arc::new(resolver),
        }
    }

    pub fn command_context(&self) -> RemoteCommandContext {
        HttpCommandContext::new(self.transport.clone(), self.resolver.clone())
    }

    pub async fn execute_command<T>(
        &self,
        command: &T,
    ) -> Result<CommandExecutionResult, ExecuteCommandError>
    where
        T: Command + Send + Sync,
    {
        let context = self.command_context();
        let output = command.handle(&context).await?;
        let Some(output) = output else {
            return Ok(successful_noop_command_result());
        };

        let commit_request = build_commit_request_from_output(&context, output).await?;
        post_commit_request(&self.transport, &commit_request).await
    }

    pub async fn execute_query<Q, R>(&self, query: &Q) -> anyhow::Result<R>
    where
        Q: Query + Serialize + Send + Sync,
        R: DeserializeOwned,
    {
        let request = build_query_request::<Q>(query)?;
        let value = execute_query_request(&self.transport, request).await?;
        Ok(serde_json::from_value(value)?)
    }

    pub async fn execute_list_query<Q, R>(&self, query: &Q) -> anyhow::Result<R>
    where
        Q: ListQuery + Serialize + Send + Sync,
        R: DeserializeOwned,
    {
        let request = build_list_query_request::<Q>(query)?;
        let value = execute_list_query_request(&self.transport, request).await?;
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
            return Ok(successful_noop_command_result());
        };

        post_commit_request(&self.transport, &commit_request).await
    }

    async fn execute_query_value(&self, request: SerializedQueryRequest) -> anyhow::Result<Value> {
        execute_query_request(&self.transport, request).await
    }

    async fn execute_list_query_value(
        &self,
        request: SerializedQueryRequest,
    ) -> anyhow::Result<Value> {
        execute_list_query_request(&self.transport, request).await
    }
}

fn build_default_http_client(options: &HttpSekibanExecutorOptions) -> Client {
    Client::builder()
        .timeout(options.request_timeout)
        .connect_timeout(options.connect_timeout)
        .build()
        .expect("default HTTP client configuration should be valid")
}

fn build_query_request<Q>(query: &Q) -> anyhow::Result<SerializedQueryRequest>
where
    Q: Query + Serialize,
{
    Ok(SerializedQueryRequest {
        query_type: Q::QUERY_TYPE.to_string(),
        query_params_json: serde_json::to_string(query)?,
        wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
    })
}

fn build_list_query_request<Q>(query: &Q) -> anyhow::Result<SerializedQueryRequest>
where
    Q: ListQuery + Serialize,
{
    Ok(SerializedQueryRequest {
        query_type: Q::QUERY_TYPE.to_string(),
        query_params_json: serde_json::to_string(query)?,
        wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
    })
}

fn successful_noop_command_result() -> CommandExecutionResult {
    CommandExecutionResult {
        status_code: 200,
        success: true,
        sortable_unique_id: None,
        response_body: serde_json::json!({ "success": true }),
    }
}

async fn post_commit_request(
    transport: &HttpSekibanTransport,
    commit_request: &CommitRequest,
) -> Result<CommandExecutionResult, ExecuteCommandError> {
    let response = transport
        .post_with_retry("/api/sekiban/serialized/commit", commit_request)
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

async fn execute_query_request(
    transport: &HttpSekibanTransport,
    request: SerializedQueryRequest,
) -> anyhow::Result<Value> {
    let response = transport
        .post_with_retry("/api/sekiban/serialized/query", &request)
        .await
        .context("serialized query request failed")?;
    let body: SerializedQueryResponse =
        deserialize_success_json(response, "serialized query request").await?;
    Ok(serde_json::from_str(&body.result_json)?)
}

async fn execute_list_query_request(
    transport: &HttpSekibanTransport,
    request: SerializedQueryRequest,
) -> anyhow::Result<Value> {
    let response = transport
        .post_with_retry("/api/sekiban/serialized/list-query", &request)
        .await
        .context("serialized list query request failed")?;
    let body: SerializedListQueryResponse =
        deserialize_success_json(response, "serialized list-query request").await?;
    Ok(serde_json::from_str(&body.items_json)?)
}

async fn deserialize_success_json<T: DeserializeOwned>(
    response: reqwest::Response,
    operation: &str,
) -> anyhow::Result<T> {
    let status = response.status();
    if !status.is_success() {
        let body = response
            .text()
            .await
            .unwrap_or_else(|err| format!("<failed to read error body: {err}>"));
        return Err(format_unsuccessful_response(operation, status, &body));
    }

    response
        .json::<T>()
        .await
        .with_context(|| format!("invalid {operation} response"))
}

fn format_unsuccessful_response(operation: &str, status: StatusCode, body: &str) -> anyhow::Error {
    let detail = extract_error_detail(body).unwrap_or_else(|| {
        let trimmed = body.trim();
        if trimmed.is_empty() {
            "<empty response body>".to_string()
        } else {
            trimmed.to_string()
        }
    });

    anyhow!("{operation} failed with status {status}: {detail}")
}

fn extract_error_detail(body: &str) -> Option<String> {
    let value = serde_json::from_str::<Value>(body).ok()?;
    value
        .get("error")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
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
pub struct TagLatestSortableRequest {
    pub tag: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TagLatestSortableResponse {
    pub exists: bool,
    pub last_sortable_unique_id: String,
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
    use axum::{extract::State, routing::post, Json, Router};
    use base64::engine::general_purpose::STANDARD;
    use sekiban_core::{CommandHandler, EventPayload};
    use serde::{Deserialize, Serialize};
    use std::net::SocketAddr;
    use std::sync::{Arc, Mutex};
    use tokio::task::JoinHandle;

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

    #[test]
    fn transport_clamps_retry_attempts_to_one() {
        let transport = HttpSekibanTransport::new(
            "http://localhost:5000",
            Client::new(),
            HttpSekibanExecutorOptions {
                max_attempts: 0,
                retry_delay: Duration::from_millis(1),
                request_timeout: Duration::from_secs(30),
                connect_timeout: Duration::from_secs(5),
            },
        );

        assert_eq!(transport.options.max_attempts, 1);
    }

    #[test]
    fn executor_options_restore_default_timeouts_when_zero() {
        let normalized = HttpSekibanExecutorOptions {
            max_attempts: 1,
            retry_delay: Duration::from_millis(1),
            request_timeout: Duration::ZERO,
            connect_timeout: Duration::ZERO,
        }
        .normalized();

        assert_eq!(normalized.request_timeout, Duration::from_secs(30));
        assert_eq!(normalized.connect_timeout, Duration::from_secs(5));
    }

    #[test]
    fn format_unsuccessful_response_prefers_json_error_field() {
        let error = format_unsuccessful_response(
            "serialized query request",
            StatusCode::BAD_REQUEST,
            r#"{"error":"projector is not registered"}"#,
        );

        assert_eq!(
            error.to_string(),
            "serialized query request failed with status 400 Bad Request: projector is not registered"
        );
    }

    #[test]
    fn format_unsuccessful_response_falls_back_to_plain_text_body() {
        let error = format_unsuccessful_response(
            "serialized list-query request",
            StatusCode::INTERNAL_SERVER_ERROR,
            "unexpected failure",
        );

        assert_eq!(
            error.to_string(),
            "serialized list-query request failed with status 500 Internal Server Error: unexpected failure"
        );
    }

    #[tokio::test]
    async fn remote_executor_executes_command_handler_and_posts_commit() {
        let server = TestServer::spawn(TestServerState::default()).await;
        let executor = RemoteSekibanExecutor::new(
            server.base_url(),
            StaticTagProjectorResolver::new().with_tag_group("weather", "WeatherProjector"),
        );

        let result = executor
            .execute_command(&CreateWeatherCommand {
                forecast_id: "f-1".to_string(),
            })
            .await
            .expect("command should succeed");

        assert!(result.success);
        assert_eq!(result.sortable_unique_id.as_deref(), Some("sort-123"));

        let requests = server.requests();
        assert!(requests
            .iter()
            .any(|req| req.path == "/api/sekiban/serialized/tag-state"));
        let commit = requests
            .iter()
            .find(|req| req.path == "/api/sekiban/serialized/commit")
            .expect("commit request should be recorded");
        assert_eq!(
            commit.body["eventCandidates"][0]["eventPayloadName"],
            "WeatherCreated"
        );
        assert_eq!(commit.body["consistencyTags"][0]["tag"], "weather:f-1");
        assert_eq!(
            commit.body["consistencyTags"][0]["lastSortableUniqueId"],
            "sort-000"
        );
    }

    #[tokio::test]
    async fn command_context_tag_exists_uses_latest_sortable_endpoint() {
        let state = TestServerState {
            latest_sortable: Some("sort-456".to_string()),
            ..Default::default()
        };
        let server = TestServer::spawn(state).await;
        let executor = RemoteSekibanExecutor::new(
            server.base_url(),
            StaticTagProjectorResolver::new().with_tag_group("weather", "WeatherProjector"),
        );

        let exists = executor
            .command_context()
            .tag_exists(&WeatherTag {
                forecast_id: "f-1".to_string(),
            })
            .await
            .expect("tag exists should succeed");

        assert!(exists);
        let requests = server.requests();
        assert!(requests
            .iter()
            .any(|req| req.path == "/api/sekiban/serialized/tag-latest-sortable"));
    }

    #[derive(Debug, Clone, Serialize, Deserialize)]
    struct WeatherTag {
        forecast_id: String,
    }

    impl Tag for WeatherTag {
        const TAG_GROUP: &'static str = "weather";

        fn tag_id(&self) -> String {
            self.forecast_id.clone()
        }
    }

    #[derive(Debug, Clone, Default, Serialize, Deserialize)]
    struct WeatherState {
        exists: bool,
    }

    impl StatePayload for WeatherState {
        const STATE_TYPE: &'static str = "WeatherState";

        fn is_empty(&self) -> bool {
            !self.exists
        }
    }

    #[derive(Debug, Clone, Serialize, Deserialize)]
    struct WeatherCreated {
        forecast_id: String,
    }

    impl EventPayload for WeatherCreated {
        const EVENT_TYPE: &'static str = "WeatherCreated";
    }

    #[derive(Debug, Clone, Serialize, Deserialize)]
    struct CreateWeatherCommand {
        forecast_id: String,
    }

    impl CommandMeta for CreateWeatherCommand {
        const COMMAND_TYPE: &'static str = "CreateWeatherCommand";
    }

    #[async_trait]
    impl CommandHandler for CreateWeatherCommand {
        async fn handle<C: CommandContext + ?Sized>(
            &self,
            context: &C,
        ) -> Result<Option<CommandOutput>, CommandError> {
            let tag = WeatherTag {
                forecast_id: self.forecast_id.clone(),
            };
            let (state, version): (WeatherState, i32) = context.get_state(&tag).await?;
            if !state.is_empty() {
                return Err(CommandError::AlreadyExists(self.forecast_id.clone()));
            }

            let output = CommandOutput::single(
                WeatherCreated {
                    forecast_id: self.forecast_id.clone(),
                },
                tag.clone(),
            )
            .map_err(|err| CommandError::Serialization(err.to_string()))?
            .with_expected_version(tag, version);
            Ok(Some(output))
        }
    }

    #[derive(Clone, Debug)]
    struct LoggedRequest {
        path: String,
        body: Value,
    }

    #[derive(Clone, Default)]
    struct TestServerState {
        requests: Arc<Mutex<Vec<LoggedRequest>>>,
        latest_sortable: Option<String>,
    }

    struct TestServer {
        address: SocketAddr,
        state: TestServerState,
        handle: JoinHandle<()>,
    }

    impl TestServer {
        async fn spawn(state: TestServerState) -> Self {
            let listener = tokio::net::TcpListener::bind("127.0.0.1:0")
                .await
                .expect("listener should bind");
            let address = listener
                .local_addr()
                .expect("listener should provide local address");
            let app = Router::new()
                .route("/api/sekiban/serialized/tag-state", post(handle_tag_state))
                .route(
                    "/api/sekiban/serialized/tag-latest-sortable",
                    post(handle_latest_sortable),
                )
                .route("/api/sekiban/serialized/commit", post(handle_commit))
                .with_state(state.clone());
            let handle = tokio::spawn(async move {
                axum::serve(listener, app)
                    .await
                    .expect("test server should serve");
            });

            Self {
                address,
                state,
                handle,
            }
        }

        fn base_url(&self) -> String {
            format!("http://{}", self.address)
        }

        fn requests(&self) -> Vec<LoggedRequest> {
            self.state
                .requests
                .lock()
                .expect("requests lock should succeed")
                .clone()
        }
    }

    impl Drop for TestServer {
        fn drop(&mut self) {
            self.handle.abort();
        }
    }

    async fn handle_tag_state(
        State(state): State<TestServerState>,
        Json(body): Json<Value>,
    ) -> Json<SerializableTagStateResponse> {
        push_request(&state, "/api/sekiban/serialized/tag-state", body);
        Json(SerializableTagStateResponse {
            payload: STANDARD.encode(r#"{"exists":false}"#),
            version: 0,
            last_sorted_unique_id: "sort-000".to_string(),
        })
    }

    async fn handle_latest_sortable(
        State(state): State<TestServerState>,
        Json(body): Json<Value>,
    ) -> Json<TagLatestSortableResponse> {
        push_request(&state, "/api/sekiban/serialized/tag-latest-sortable", body);
        Json(TagLatestSortableResponse {
            exists: state.latest_sortable.is_some(),
            last_sortable_unique_id: state.latest_sortable.unwrap_or_default(),
        })
    }

    async fn handle_commit(
        State(state): State<TestServerState>,
        Json(body): Json<Value>,
    ) -> Json<Value> {
        push_request(&state, "/api/sekiban/serialized/commit", body);
        Json(serde_json::json!({
            "writtenEvents": [
                {
                    "sortableUniqueIdValue": "sort-123"
                }
            ]
        }))
    }

    fn push_request(state: &TestServerState, path: &str, body: Value) {
        state
            .requests
            .lock()
            .expect("requests lock should succeed")
            .push(LoggedRequest {
                path: path.to_string(),
                body,
            });
    }
}
