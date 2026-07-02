// Query helpers for Sekiban AssemblyScript projector modules.

export function applyPaging<T>(items: T[], pageSize: i32, pageNumber: i32): T[] {
  if (pageSize <= 0) return items;
  const page = pageNumber > 0 ? pageNumber : 1;
  const start = (page - 1) * pageSize;
  if (start >= items.length) return [];
  const end = start + pageSize > items.length ? items.length : start + pageSize;
  return items.slice(start, end);
}
