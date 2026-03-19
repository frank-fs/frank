namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Text.Json
open Frank.Statecharts.Unified
open Frank.Cli.Core.Unified
open Frank.Statecharts.Validation

module OpenApiValidateCommand =

    type OpenApiValidateResult =
        { Report: ValidationReport
          Discrepancies: OpenApiConsistencyValidator.FieldDiscrepancy list }

    /// Execute OpenAPI consistency validation.
    let execute
        (projectPath: string)
        (openApiDocPath: string)
        : Result<OpenApiValidateResult, string> =

        let projectDir = Path.GetDirectoryName projectPath

        // Load unified extraction state
        let unifiedPath =
            Path.Combine(projectDir, "obj", "frank-cli", "descriptors.bin")

        if not (File.Exists unifiedPath) then
            Result.Error
                "No unified extraction state found. Run: frank-cli semantic extract --project <fsproj>"
        else

        // Load the OpenAPI document
        if not (File.Exists openApiDocPath) then
            Result.Error $"OpenAPI document not found at: %s{openApiDocPath}"
        else

        try
            let openApiJson = File.ReadAllText openApiDocPath
            let doc = JsonDocument.Parse openApiJson

            // Navigate to components/schemas
            let schemasElement =
                match doc.RootElement.TryGetProperty("components") with
                | true, components ->
                    match components.TryGetProperty("schemas") with
                    | true, schemas -> Some schemas
                    | _ -> None
                | _ -> None

            match schemasElement with
            | None ->
                Result.Error
                    "OpenAPI document does not contain 'components/schemas' section"
            | Some schemas ->
                // Load unified types from the extraction state
                // For now, we deserialize the unified state to get types
                let bytes = File.ReadAllBytes unifiedPath

                let unified =
                    MessagePack.MessagePackSerializer.Deserialize<UnifiedExtractionState>(
                        bytes,
                        MessagePack.Resolvers.ContractlessStandardResolverAllowPrivate.Options
                    )

                let allTypes =
                    unified.Resources
                    |> List.collect (fun r -> r.TypeInfo)
                    |> List.distinctBy (fun t -> t.FullName)

                let result = OpenApiConsistencyValidator.validate allTypes schemas
                let report = OpenApiConsistencyValidator.toValidationReport result

                Result.Ok
                    { Report = report
                      Discrepancies = result.Discrepancies }
        with ex ->
            Result.Error $"Failed to parse OpenAPI document: %s{ex.Message}"
