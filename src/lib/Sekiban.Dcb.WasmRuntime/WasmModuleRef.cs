namespace Sekiban.Dcb.WasmRuntime;

public record WasmModuleRef(
    string ProjectorName,
    string ModulePath,
    string AbiKind,
    string ModuleVersion,
    string ProjectorVersion,
    string? TagPayloadName = null);
