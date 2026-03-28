module Frank.Cli.Core.Statechart.StatechartError

open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.FormatDetector

type StatechartError =
    | AssemblyLoadError of path: string * reason: string
    | UnknownFormat of format: string
    | UnsupportedFileExtension of extension: string * filePath: string
    | AmbiguousFileExtension of filePath: string * candidates: FormatTag list
    | FileNotFound of path: string
    | FileReadError of path: string * reason: string
    | AmbiguousParseFailed of filePath: string * attempts: string list
    | GenerationFailed of format: FormatTag * resourceSlug: string * reason: string
    | ResourceNotFound of name: string * available: string list

let formatError (error: StatechartError) : string =
    match error with
    | AssemblyLoadError(path, reason) -> sprintf "Unexpected error loading '%s': %s" path reason
    | UnknownFormat fmt ->
        sprintf "Unknown format: '%s'. Supported: wsd, alps, alps-xml, scxml, smcat, xstate, affordance-map" fmt
    | UnsupportedFileExtension(ext, path) ->
        sprintf
            "Unsupported file extension '%s' for '%s'. Supported: .wsd, .alps.json, .alps.xml, .scxml, .smcat, .xstate.json"
            ext
            path
    | AmbiguousFileExtension(path, _) ->
        sprintf
            "Ambiguous file extension for '%s'. Use a compound extension (.alps.json, .xstate.json) or --format to disambiguate."
            path
    | FileNotFound path -> sprintf "File not found: %s" path
    | FileReadError(path, reason) -> sprintf "Cannot read file '%s': %s" path reason
    | AmbiguousParseFailed(path, attempts) ->
        sprintf "Could not parse '%s' as any supported format. Attempts: %s" path (attempts |> String.concat "; ")
    | GenerationFailed(format, slug, reason) ->
        sprintf "Failed to generate %s for resource '%s': %s" (FormatTag.toString format) slug reason
    | ResourceNotFound(name, available) ->
        sprintf "No resource matching '%s' found. Available: %s" name (available |> String.concat ", ")

let formatErrorJson (error: StatechartError) : string =
    let errorCode =
        match error with
        | AssemblyLoadError _ -> "assembly_load_error"
        | UnknownFormat _ -> "unknown_format"
        | UnsupportedFileExtension _ -> "unsupported_extension"
        | AmbiguousFileExtension _ -> "ambiguous_extension"
        | FileNotFound _ -> "file_not_found"
        | FileReadError _ -> "file_read_error"
        | AmbiguousParseFailed _ -> "ambiguous_parse_failed"
        | GenerationFailed _ -> "generation_failed"
        | ResourceNotFound _ -> "resource_not_found"

    use stream = new System.IO.MemoryStream()

    use writer =
        new System.Text.Json.Utf8JsonWriter(stream, System.Text.Json.JsonWriterOptions(Indented = true))

    writer.WriteStartObject()
    writer.WriteString("status", "error")
    writer.WriteString("code", errorCode)
    writer.WriteString("message", formatError error)
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())
