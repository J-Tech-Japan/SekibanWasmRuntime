// Types for communicating with the Sekiban WASM runtime server.
// These mirror the C# SerializedDcbClient contracts.

pub(all) struct TagStateRequest {
  tagStateId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct TagStateResponse {
  payload : String   // base64-encoded state JSON
  version : Int
  lastSortableUniqueId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct CommitEventCandidate {
  eventPayloadName : String
  payloadBase64 : String
  tags : Array[String]
} derive(ToJson, @json.FromJson, Show)

pub(all) struct ConsistencyTagEntry {
  tag : String
  lastSortableUniqueId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct CommitRequest {
  eventCandidates : Array[CommitEventCandidate]
  consistencyTags : Array[ConsistencyTagEntry]
} derive(ToJson, @json.FromJson, Show)

pub(all) struct CommitResponse {
  eventId : String
  sortableUniqueId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct QueryRequest {
  queryType : String
  queryParamsJson : String
  waitForSortableUniqueId : String?
} derive(ToJson, @json.FromJson, Show)

pub(all) struct QueryResponse {
  result : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct ListQueryResponse {
  items : String
  totalCount : Int?
} derive(ToJson, @json.FromJson, Show)

pub(all) struct LoadedTagState {
  tagGroup : String
  tagContent : String
  projectorName : String
  payload : String    // decoded state JSON
  version : Int
  lastSortableUniqueId : String
} derive(ToJson, @json.FromJson, Show)
