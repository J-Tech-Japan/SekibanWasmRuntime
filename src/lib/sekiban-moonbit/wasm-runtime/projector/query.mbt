pub fn matches_optional_filter(value : String, filter : String?) -> Bool {
  match filter {
    None => true
    Some(f) => value.to_lower().contains(f.to_lower())
  }
}

pub fn[T] apply_paging(items : Array[T], page_number : Int?, page_size : Int?) -> Array[T] {
  match page_size {
    None => items
    Some(ps) => {
      if ps <= 0 {
        return items
      }
      let pn = match page_number {
        None => 1
        Some(v) => if v > 0 { v } else { 1 }
      }
      let start = (pn - 1) * ps
      if start >= items.length() {
        []
      } else {
        items[start:start + ps].to_array()
      }
    }
  }
}
