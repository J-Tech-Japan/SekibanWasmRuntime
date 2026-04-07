pub struct EventPayload {
  eventType : String
  payload : String
} derive(ToJson, FromJson, Show, Eq)

pub struct CommandOutput {
  events : Array[EventPayload]
  tags : Array[String]
  consistencyTags : Array[String]
  expectedVersions : Map[String, Int]
} derive(ToJson, FromJson, Show, Eq)

pub struct WasmResult {
  ok : Bool
  status : Int
  error : String?
  message : String?
  output : CommandOutput?
} derive(ToJson, FromJson, Show, Eq)

pub struct WasmEventMetadata {
  tags : Array[String]?
  sortableUniqueId : String?
} derive(ToJson, FromJson, Show, Eq)
