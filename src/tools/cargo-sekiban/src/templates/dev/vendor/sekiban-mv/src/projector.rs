//! Trait a WASM-side materialized view projector implements.
//!
//! Mirrors the C# `IWasmMvProjector` interface one-for-one so the `ClassRoomEnrollmentMvV1`
//! implementations stay structurally identical between languages.

use crate::dto::{MvSerializableEventDto, MvSqlStatementDto, MvTableBindingsDto};
use crate::query_port::MvQueryPort;

pub trait WasmMvProjector: Send + Sync + 'static {
    fn view_name(&self) -> &'static str;
    fn view_version(&self) -> i32;
    fn logical_tables(&self) -> &'static [&'static str];

    fn initialize(&self, tables: &MvTableBindingsDto) -> Vec<MvSqlStatementDto>;

    fn apply_event(
        &self,
        tables: &MvTableBindingsDto,
        event: &MvSerializableEventDto,
        query_port: &dyn MvQueryPort,
    ) -> Vec<MvSqlStatementDto>;
}
