namespace Frank.Cli.Core.State

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing

type SourceLocation =
    { File: string; Line: int; Column: int }

type ExtractionMetadata =
    { Timestamp: DateTimeOffset
      SourceHash: string
      ToolVersion: string
      BaseUri: Uri
      Vocabularies: string list }

type UnmappedType =
    { TypeName: string
      Reason: string
      Location: SourceLocation }

type ExtractionState =
    { Ontology: IGraph
      Shapes: IGraph
      SourceMap: Map<string, SourceLocation>
      Clarifications: Map<string, string>
      Metadata: ExtractionMetadata
      UnmappedTypes: UnmappedType list }

module ExtractionState =

    let graphToTurtle (graph: IGraph) : string =
        let sw = new System.IO.StringWriter()
        let writer = CompressingTurtleWriter()
        writer.Save(graph, sw :> System.IO.TextWriter)
        sw.ToString()

    let turtleToGraph (turtle: string) : IGraph =
        let g = new Graph()
        let parser = TurtleParser()
        let sr = new System.IO.StringReader(turtle)
        parser.Load(g, sr :> System.IO.TextReader)
        g :> IGraph

    let private writeSourceLocation (writer: Utf8JsonWriter) (loc: SourceLocation) =
        writer.WriteStartObject()
        writer.WriteString("file", loc.File)
        writer.WriteNumber("line", loc.Line)
        writer.WriteNumber("column", loc.Column)
        writer.WriteEndObject()

    let private readSourceLocation (elem: JsonElement) : SourceLocation =
        { File = elem.GetProperty("file").GetString()
          Line = elem.GetProperty("line").GetInt32()
          Column = elem.GetProperty("column").GetInt32() }

    let save (path: string) (state: ExtractionState) : Result<unit, string> =
        try
            let dir = Path.GetDirectoryName(path)

            if not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            use stream = File.Create(path)

            use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

            writer.WriteStartObject()

            // Graphs as Turtle strings
            writer.WriteString("ontology", graphToTurtle state.Ontology)
            writer.WriteString("shapes", graphToTurtle state.Shapes)

            // SourceMap
            writer.WriteStartObject("sourceMap")

            for kvp in state.SourceMap do
                writer.WritePropertyName(kvp.Key)
                writeSourceLocation writer kvp.Value

            writer.WriteEndObject()

            // Clarifications
            writer.WriteStartObject("clarifications")

            for kvp in state.Clarifications do
                writer.WriteString(kvp.Key, kvp.Value)

            writer.WriteEndObject()

            // Metadata
            writer.WriteStartObject("metadata")
            writer.WriteString("timestamp", state.Metadata.Timestamp)
            writer.WriteString("sourceHash", state.Metadata.SourceHash)
            writer.WriteString("toolVersion", state.Metadata.ToolVersion)
            writer.WriteString("baseUri", state.Metadata.BaseUri.AbsoluteUri)
            writer.WriteStartArray("vocabularies")

            for v in state.Metadata.Vocabularies do
                writer.WriteStringValue(v)

            writer.WriteEndArray()
            writer.WriteEndObject()

            // UnmappedTypes
            writer.WriteStartArray("unmappedTypes")

            for ut in state.UnmappedTypes do
                writer.WriteStartObject()
                writer.WriteString("typeName", ut.TypeName)
                writer.WriteString("reason", ut.Reason)
                writer.WritePropertyName("location")
                writeSourceLocation writer ut.Location
                writer.WriteEndObject()

            writer.WriteEndArray()

            writer.WriteEndObject()
            writer.Flush()
            Ok()
        with ex ->
            Error ex.Message

    let load (path: string) : Result<ExtractionState, string> =
        try
            if not (File.Exists(path)) then
                Error $"File not found: {path}"
            else
                let json = File.ReadAllText(path)
                let doc = JsonDocument.Parse(json)
                let root = doc.RootElement

                let ontology = turtleToGraph (root.GetProperty("ontology").GetString())
                let shapes = turtleToGraph (root.GetProperty("shapes").GetString())

                let sourceMap =
                    let sm = root.GetProperty("sourceMap")
                    let mutable map = Map.empty

                    for prop in sm.EnumerateObject() do
                        let loc = readSourceLocation prop.Value
                        // Handle both old (Uri) and new (string) key formats
                        map <- Map.add prop.Name loc map

                    map

                let clarifications =
                    let cl = root.GetProperty("clarifications")
                    let mutable map = Map.empty

                    for prop in cl.EnumerateObject() do
                        map <- Map.add prop.Name (prop.Value.GetString()) map

                    map

                let metaElem = root.GetProperty("metadata")

                let metadata =
                    { Timestamp = metaElem.GetProperty("timestamp").GetDateTimeOffset()
                      SourceHash = metaElem.GetProperty("sourceHash").GetString()
                      ToolVersion = metaElem.GetProperty("toolVersion").GetString()
                      BaseUri = Uri(metaElem.GetProperty("baseUri").GetString())
                      Vocabularies =
                        [ for v in metaElem.GetProperty("vocabularies").EnumerateArray() do
                              v.GetString() ] }

                let unmappedTypes =
                    [ for ut in root.GetProperty("unmappedTypes").EnumerateArray() do
                          { TypeName = ut.GetProperty("typeName").GetString()
                            Reason = ut.GetProperty("reason").GetString()
                            Location = readSourceLocation (ut.GetProperty("location")) } ]

                Ok
                    { Ontology = ontology
                      Shapes = shapes
                      SourceMap = sourceMap
                      Clarifications = clarifications
                      Metadata = metadata
                      UnmappedTypes = unmappedTypes }
        with ex ->
            Error ex.Message

    let defaultStatePath (projectDir: string) : string =
        Path.Combine(projectDir, "obj", "frank-cli", "state.json")
