package mv

// QueryPort is the minimal interface a projector uses to consult already-projected rows
// mid-apply. The default HostBackedQueryPort routes through the env.mv_host_query_rows host
// import; projectors that do not query can ignore the argument entirely.
type QueryPort interface {
	QueryRows(sql string, params []MvParam) []MvQueryRowDto
	QuerySingleRow(sql string, params []MvParam) *MvQueryRowDto
}

// Projector is what each materialized-view implementation satisfies. Mirrors the Rust
// `WasmMvProjector` trait and the Swift protocol.
type Projector interface {
	ViewName() string
	ViewVersion() int32
	LogicalTables() []string

	Initialize(tables MvTableBindingsDto) []MvSqlStatementDto
	ApplyEvent(
		tables MvTableBindingsDto,
		event MvSerializableEventDto,
		queryPort QueryPort,
	) []MvSqlStatementDto
}
