namespace Frank.Cli.Core.Output

open System.Text.Json

module JsonOutput =

    let private options =
        JsonSerializerOptions(
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true)

    type UnmappedTypeJson =
        { TypeName: string
          Reason: string
          File: string
          Line: int }

    type ExtractResultJson =
        { Status: string
          ClassCount: int
          PropertyCount: int
          ShapeCount: int
          UnmappedTypes: UnmappedTypeJson list
          StateFilePath: string }

    type QuestionOptionJson =
        { Label: string
          Impact: string }

    type QuestionContextJson =
        { SourceType: string
          Location: string }

    type QuestionJson =
        { Id: string
          Category: string
          QuestionText: string
          Context: QuestionContextJson
          Options: QuestionOptionJson list }

    type ClarifyResultJson =
        { Status: string
          Questions: QuestionJson list
          ResolvedCount: int
          TotalCount: int }

    let formatExtractResult (result: ExtractResultJson) : string =
        JsonSerializer.Serialize(result, options)

    let formatClarifyResult (result: ClarifyResultJson) : string =
        JsonSerializer.Serialize(result, options)

    let formatError (message: string) : string =
        let error = {| status = "error"; message = message |}
        JsonSerializer.Serialize(error, options)
