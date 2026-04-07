pub fn is_empty_json(s : String) -> Bool {
  let t = s.trim()
  t == "" || t == "{}" || t == "null"
}

pub fn json_stringify(j : Json) -> String {
  j.stringify()
}

pub fn[T : @json.FromJson] parse_json(s : String, fallback : T) -> T {
  if is_empty_json(s) {
    return fallback
  }
  try {
    let j = @json.parse(s[:])
    let v : T = @json.from_json(j)
    v
  } catch {
    _ => fallback
  }
}

pub fn contains(items : Array[String], v : String) -> Bool {
  for x in items {
    if x == v {
      return true
    }
  }
  false
}

pub fn remove(items : Array[String], v : String) -> Array[String] {
  let out : Array[String] = []
  for x in items {
    if x != v {
      out.push(x)
    }
  }
  out
}

pub fn add_unique(items : Array[String], v : String) -> Array[String] {
  if contains(items, v) {
    return items
  }
  let out : Array[String] = []
  for x in items {
    out.push(x)
  }
  out.push(v)
  out
}

pub fn option_string(value : String?) -> String {
  match value {
    None => ""
    Some(v) => v
  }
}

pub fn tag_string(tag_group : String, tag_id : String) -> String {
  tag_group + ":" + tag_id
}
