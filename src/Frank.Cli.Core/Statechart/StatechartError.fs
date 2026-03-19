module Frank.Cli.Core.Statechart.StatechartError

open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.FormatDetector

type StatechartError =
    | AssemblyNotFound of path: string
    | AssemblyLoadFailed of path: string * reason: string
    | InvalidAssemblyFormat of path: string * reason: string
    | MissingDependency of assemblyName: string * reason: string
    | AssemblyLoadError of path: string * reason: string
    | ExtractionNotImplemented
    | UnknownFormat of format: string
    | UnsupportedFileExtension of extension: string * filePath: string
    | AmbiguousFileExtension of filePath: string * candidates: FormatTag list
    | FileNotFound of path: string
    | FileReadError of path: string * reason: string
    | ParseFailed of filePath: string * errors: string list
    | AmbiguousParseFailed of filePath: string * attempts: string list
    | GenerationFailed of format: FormatTag * resourceSlug: string * reason: string
    | UnrecognizedMachineType of typeName: string
    | ResourceNotFound of name: string * available: string list
    | CodeTruthExtractionFailed of reason: string

let formatError (error: StatechartError) : string =
    match error with
    | AssemblyNotFound path -> sprintf "Assembly not found: %s" path
    | AssemblyLoadFailed(path, reason) -> sprintf "Failed to load assembly '%s': %s" path reason
    | InvalidAssemblyFormat(path, reason) -> sprintf "Invalid assembly format '%s': %s" path reason
    | MissingDependency(name, reason) -> sprintf "Missing dependency '%s': %s" name reason
    | AssemblyLoadError(path, reason) -> sprintf "Unexpected error loading '%s': %s" path reason
    | ExtractionNotImplemented ->
        "Assembly-based extraction is not yet implemented. Use 'statechart parse' to parse spec files directly."
    | UnknownFormat fmt ->
        sprintf "Unknown format: '%s'. Supported: wsd, alps, alps-xml, scxml, smcat, xstate, affordance-map" fmt
    | UnsupportedFileExtension(ext, path) ->
        sprintf "Unsupported file extension '%s' for '%s'. Supported: .wsd, .alps.json, .alps.xml, .scxml, .smcat, .xstate.json" ext path
    | AmbiguousFileExtension(path, _) ->
        sprintf "Ambiguous file extension for '%s'. Use a compound extension (.alps.json, .xstate.json) or --format to disambiguate." path
    | FileNotFound path -> sprintf "File not found: %s" path
    | FileReadError(path, reason) -> sprintf "Cannot read file '%s': %s" path reason
    | ParseFailed(path, errors) ->
        sprintf "Parse errors in '%s': %s" path (errors |> String.concat "; ")
    | AmbiguousParseFailed(path, attempts) ->
        sprintf "Could not parse '%s' as any supported format. Attempts: %s" path (attempts |> String.concat "; ")
    | GenerationFailed(format, slug, reason) ->
        sprintf "Failed to generate %s for resource '%s': %s" (FormatTag.toString format) slug reason
    | UnrecognizedMachineType typeName ->
        sprintf "Unrecognized machine type: %s" typeName
    | ResourceNotFound(name, available) ->
        sprintf "No resource matching '%s' found. Available: %s" name (available |> String.concat ", ")
    | CodeTruthExtractionFailed reason ->
        sprintf "Code-truth extraction failed: %s" reason

let formatErrorJson (error: StatechartError) : string =
    let errorCode =
        match error with
        | AssemblyNotFound _ -> "assembly_not_found"
        | AssemblyLoadFailed _ -> "assembly_load_failed"
        | InvalidAssemblyFormat _ -> "invalid_assembly_format"
        | MissingDependency _ -> "missing_dependency"
        | AssemblyLoadError _ -> "assembly_load_error"
        | ExtractionNotImplemented -> "extraction_not_implemented"
        | UnknownFormat _ -> "unknown_format"
        | UnsupportedFileExtension _ -> "unsupported_extension"
        | AmbiguousFileExtension _ -> "ambiguous_extension"
        | FileNotFound _ -> "file_not_found"
        | FileReadError _ -> "file_read_error"
        | ParseFailed _ -> "parse_failed"
        | AmbiguousParseFailed _ -> "ambiguous_parse_failed"
        | GenerationFailed _ -> "generation_failed"
        | UnrecognizedMachineType _ -> "unrecognized_machine_type"
        | ResourceNotFound _ -> "resource_not_found"
        | CodeTruthExtractionFailed _ -> "code_truth_extraction_failed"

    use stream = new System.IO.MemoryStream()
    use writer = new System.Text.Json.Utf8JsonWriter(stream, System.Text.Json.JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("status", "error")
    writer.WriteString("code", errorCode)
    writer.WriteString("message", formatError error)
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())
