// Types for communicating with the Sekiban WASM runtime server.
// These mirror the C# SerializedDcbClient contracts.

pub(all) struct TagStateRequest {
  tagStateId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct TagStateResponse {
  payload : String   // base64-encoded state JSON
  version : Int
  // Wire name is `lastSortedUniqueId`: the host serializes
  // SerializableTagState with that key (there is no lastSortableUniqueId in
  // the tag-state response), as proven by the mooncakes external-consumer
  // smoke (SWR-G065).
  lastSortedUniqueId : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct HttpError {
  status : Int
  message : String
  body : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct RequestResult[T] {
  value : T?
  error : HttpError?
}

pub(all) struct CommitEventCandidate {
  eventPayloadName : String
  // Wire name is `payload`: the host's SerializableEventCandidate binds its
  // base64 event bytes from the camelCase `payload` key (a `payloadBase64`
  // key is silently ignored and the commit fails), as proven by the
  // mooncakes external-consumer smoke (SWR-G065).
  payload : String
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

pub(all) struct WrittenEvent {
  id : String
  sortableUniqueIdValue : String
} derive(ToJson, @json.FromJson, Show)

pub(all) struct CommitResponse {
  writtenEvents : Array[WrittenEvent]
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
