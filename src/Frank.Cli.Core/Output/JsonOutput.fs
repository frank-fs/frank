namespace Frank.Cli.Core.Output

open System.IO
open System.Text
open System.Text.Json
open Frank.Cli.Core.Commands
open Frank.Cli.Core.State

module JsonOutput =

    let private serializerOptions =
        JsonSerializerOptions(
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true)

    let private writeString (w: Utf8JsonWriter) (name: string) (value: string) =
        w.WriteString(name, value)

    let private writeNumber (w: Utf8JsonWriter) (name: string) (value: int) =
        w.WriteNumber(name, value)

    let formatExtractResult (result: ExtractCommand.ExtractResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"

        writer.WriteStartObject("ontologySummary")
        writeNumber writer "classCount" result.OntologySummary.ClassCount
        writeNumber writer "propertyCount" result.OntologySummary.PropertyCount
        writeNumber writer "alignedCount" result.OntologySummary.AlignedCount
        writeNumber writer "unalignedCount" result.OntologySummary.UnalignedCount
        writer.WriteEndObject()

        writer.WriteStartObject("shapesSummary")
        writeNumber writer "shapeCount" result.ShapesSummary.ShapeCount
        writeNumber writer "constraintCount" result.ShapesSummary.ConstraintCount
        writer.WriteEndObject()

        writer.WriteStartArray("unmappedTypes")
        for ut in result.UnmappedTypes do
            writer.WriteStartObject()
            writeString writer "typeName" ut.TypeName
            writeString writer "reason" ut.Reason
            writeString writer "file" ut.Location.File
            writeNumber writer "line" ut.Location.Line
            writer.WriteEndObject()
        writer.WriteEndArray()

        writeString writer "stateFilePath" result.StateFilePath

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatClarifyResult (result: ClarifyCommand.ClarifyResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"

        writer.WriteStartArray("questions")
        for q in result.Questions do
            writer.WriteStartObject()
            writeString writer "id" q.Id
            writeString writer "category" q.Category
            writeString writer "questionText" q.QuestionText

            writer.WriteStartObject("context")
            writeString writer "sourceType" q.Context.SourceType
            match q.Context.Location with
            | Some loc -> writeString writer "location" loc
            | None -> writer.WriteNull("location")
            writer.WriteEndObject()

            writer.WriteStartArray("options")
            for opt in q.Options do
                writer.WriteStartObject()
                writeString writer "label" opt.Label
                writeString writer "impact" opt.Impact
                writer.WriteEndObject()
            writer.WriteEndArray()

            writer.WriteEndObject()
        writer.WriteEndArray()

        writeNumber writer "resolvedCount" result.ResolvedCount
        writeNumber writer "totalCount" result.TotalCount

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatError (message: string) : string =
        let error = {| status = "error"; message = message |}
        JsonSerializer.Serialize(error, serializerOptions)
