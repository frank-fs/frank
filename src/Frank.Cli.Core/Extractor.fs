module Frank.Cli.Core.Extractor

open System
open System.IO
open System.Diagnostics
open System.Xml.Linq
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Frank.Semantic

// ── attribute helpers ─────────────────────────────────────────────────────────

let private attributeShortName (attr: FSharpAttribute) : string =
    let name = attr.AttributeType.DisplayName

    if name.EndsWith("Attribute") then
        name.[.. name.Length - 10]
    else
        name

let private firstConstructorArgAsString (attr: FSharpAttribute) : string =
    attr.ConstructorArguments
    |> Seq.tryHead
    |> Option.map (fun (_, v) ->
        let s = sprintf "%A" v
        s.Trim('"'))
    |> Option.defaultValue ""

let private buildAttributeMap (attrs: FSharpAttribute seq) : Map<string, string> =
    attrs
    |> Seq.map (fun a -> attributeShortName a, firstConstructorArgAsString a)
    |> Map.ofSeq

// ── type name normalization ───────────────────────────────────────────────────

let private normalizeTypeName (name: string) : string =
    name
    |> fun s -> s.Replace("Microsoft.FSharp.Core.", "")
    |> fun s -> s.Replace("Microsoft.FSharp.Collections.", "")

// ── doc comment extraction ────────────────────────────────────────────────────

let private docCommentOf (xmlDoc: FSharpXmlDoc) : string option =
    match xmlDoc with
    | FSharpXmlDoc.FromXmlText xt ->
        let lines = xt.UnprocessedLines

        if lines.Length = 0 then
            None
        else
            lines
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s <> "")
            |> String.concat " "
            |> fun s -> if s = "" then None else Some s
    | _ -> None

// ── field extraction ──────────────────────────────────────────────────────────

let private fieldToFieldInfo (field: FSharpField) : FieldInfo =
    let attrs =
        Seq.append field.FieldAttributes field.PropertyAttributes |> buildAttributeMap

    { Name = field.Name
      TypeName = field.FieldType.Format FSharpDisplayContext.Empty |> normalizeTypeName
      Attributes = attrs
      DocComment = docCommentOf field.XmlDoc }

let private localTypeName (t: string) : string =
    let baseName =
        match t.IndexOf('<') with
        | -1 -> t
        | i -> t.[.. i - 1]

    match baseName.LastIndexOf('.') with
    | -1 -> baseName
    | i -> baseName.[i + 1 ..]

// Payload field name priority: explicit label > generated name fallback to type name.
// FCS gives generated names "Item", "Item1", ... for unlabeled payloads; we
// keep the FCS field name only when the author supplied a label, otherwise we
// fall back to the payload TYPE name so type-name tokens can drive the join.
let private payloadFieldInfo (ucField: FSharpField) : FieldInfo =
    let typeName =
        ucField.FieldType.Format FSharpDisplayContext.Empty |> normalizeTypeName

    let isGenerated =
        ucField.Name = "Item"
        || (ucField.Name.StartsWith("Item", StringComparison.Ordinal)
            && ucField.Name.Length > 4
            && ucField.Name.[4..] |> Seq.forall Char.IsDigit)

    let name = if isGenerated then localTypeName typeName else ucField.Name

    { Name = name
      TypeName = typeName
      Attributes = buildAttributeMap (Seq.append ucField.FieldAttributes ucField.PropertyAttributes)
      DocComment = docCommentOf ucField.XmlDoc }

let private unionCaseToCaseInfo (uc: FSharpUnionCase) : CaseInfo =
    let payload =
        if uc.HasFields then
            uc.Fields |> Seq.map payloadFieldInfo |> Seq.toList
        else
            []

    { Name = uc.Name
      Payload = payload
      Attributes = buildAttributeMap uc.Attributes
      DocComment = docCommentOf uc.XmlDoc }

// ── entity → TypeInfo ─────────────────────────────────────────────────────────

let private entityToTypeInfo (entity: FSharpEntity) : TypeInfo option =
    if entity.IsNamespace || entity.IsFSharpModule then
        None
    else
        let ns = entity.Namespace |> Option.defaultValue ""

        let shape =
            if entity.IsFSharpUnion then
                entity.UnionCases |> Seq.map unionCaseToCaseInfo |> Seq.toList |> Union
            else
                entity.FSharpFields |> Seq.map fieldToFieldInfo |> Seq.toList |> Record

        Some
            { FullName = entity.TryFullName |> Option.defaultValue entity.LogicalName
              Namespace = ns
              LocalName = entity.LogicalName
              Shape = shape
              Attributes = buildAttributeMap entity.Attributes
              DocComment = docCommentOf entity.XmlDoc }

// ── symbol walk ───────────────────────────────────────────────────────────────

let private collectFromEntity (inProject: FSharpEntity -> bool) (entity: FSharpEntity) : TypeInfo list =
    let maxDepth = 20

    let rec walk depth (e: FSharpEntity) : TypeInfo list =
        if depth > maxDepth then
            []
        else
            let nested = e.NestedEntities |> Seq.collect (walk (depth + 1)) |> Seq.toList

            if inProject e then
                match entityToTypeInfo e with
                | Some ti -> ti :: nested
                | None -> nested
            else
                nested

    walk 0 entity

// ── in-memory source extraction ───────────────────────────────────────────────

let private virtualFileName = "/tmp/frank_extract_virtual.fsx"

let private inMemoryOptions (checker: FSharpChecker) (source: ISourceText) =
    checker.GetProjectOptionsFromScript(virtualFileName, source, assumeDotNetFramework = false, useSdkRefs = true)
    |> Async.RunSynchronously

/// Extract TypeInfo records from F# source code given as a string.
/// Types from referenced assemblies are excluded; only types whose declaration
/// location resolves to the virtual source file are returned.
let extractTypeInfosFromSource (sourceCode: string) : Result<TypeInfo list, string> =
    if String.IsNullOrWhiteSpace sourceCode then
        Error "sourceCode must not be empty"
    else

        let checker = FSharpChecker.Create(keepAssemblyContents = true)
        let source = SourceText.ofString sourceCode

        let options, diagnostics = inMemoryOptions checker source

        if
            diagnostics
            |> List.exists (fun (d: FSharp.Compiler.Diagnostics.FSharpDiagnostic) ->
                d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
        then
            let msgs = diagnostics |> List.map (fun d -> d.Message) |> String.concat "; "
            Error $"Script options error: {msgs}"
        else

            let _, checkResult =
                checker.ParseAndCheckFileInProject(virtualFileName, 0, source, options)
                |> Async.RunSynchronously

            match checkResult with
            | FSharpCheckFileAnswer.Aborted -> Error "FCS check aborted"
            | FSharpCheckFileAnswer.Succeeded fileResults ->
                let virtualPath = Path.GetFullPath(virtualFileName)

                let inProject (entity: FSharpEntity) =
                    let loc = entity.DeclarationLocation.FileName

                    Path.GetFullPath(loc) = virtualPath

                let types =
                    fileResults.PartialAssemblySignature.Entities
                    |> Seq.collect (collectFromEntity inProject)
                    |> Seq.toList

                Ok types

// ── .fsproj cracking helpers ──────────────────────────────────────────────────

let private runProcess (exe: string) (args: string) (workDir: string) : int * string * string =
    let psi = ProcessStartInfo(exe, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use p = Process.Start(psi)
    let out = p.StandardOutput.ReadToEnd()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, out, err

let private readSourceFilesFromFsproj (projectFile: string) : string[] =
    let dir = Path.GetDirectoryName(projectFile)
    let doc = XDocument.Load(projectFile)

    doc.Descendants(XName.Get("Compile"))
    |> Seq.choose (fun el ->
        el.Attribute(XName.Get("Include"))
        |> Option.ofObj
        |> Option.map (fun a -> Path.GetFullPath(Path.Combine(dir, a.Value))))
    |> Seq.toArray

let private resolveReferences (projectFile: string) : string[] =
    let dir = Path.GetDirectoryName(projectFile)

    let tmpOut =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_refs.txt")

    let targetsContent =
        sprintf
            """<Project>
  <Target Name="_FrankEmitRefs" AfterTargets="ResolveAssemblyReferences">
    <WriteLinesToFile File="%s" Lines="@(ReferencePath)" Overwrite="true" />
  </Target>
</Project>"""
            (tmpOut.Replace("\\", "/"))

    let tmpTargets =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_frank.targets")

    File.WriteAllText(tmpTargets, targetsContent)

    try
        let args =
            sprintf
                "build \"%s\" /p:TargetFramework=net10.0 /p:CustomAfterMicrosoftCommonTargets=\"%s\" /nologo /v:q"
                projectFile
                tmpTargets

        let code, _out, _err = runProcess "dotnet" args dir

        if code = 0 && File.Exists(tmpOut) then
            File.ReadAllLines(tmpOut)
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(l))
        else
            [||]
    finally
        if File.Exists(tmpTargets) then
            File.Delete(tmpTargets)

        if File.Exists(tmpOut) then
            File.Delete(tmpOut)

let private buildProjectOptions (projectFile: string) (sourceFiles: string[]) (refs: string[]) : FSharpProjectOptions =
    let refArgs = refs |> Array.map (fun r -> sprintf "-r:%s" r)

    let otherOptions =
        Array.concat [ [| "--noframework"; "--warn:0"; "--targetprofile:netstandard" |]; refArgs ]

    { ProjectFileName = projectFile
      ProjectId = None
      SourceFiles = sourceFiles
      OtherOptions = otherOptions
      ReferencedProjects = [||]
      IsIncompleteTypeCheckEnvironment = false
      UseScriptResolutionRules = false
      LoadTime = DateTime.Now
      UnresolvedReferences = None
      OriginalLoadReferences = []
      Stamp = None }

// ── .fsproj wrapper ───────────────────────────────────────────────────────────

/// Extract TypeInfo records for all F# record and DU types defined in the
/// project's own source files. Cross-project / NuGet types are excluded.
let extractTypeInfos (projectFile: string) : Result<TypeInfo list, string> =
    let projectFile = Path.GetFullPath(projectFile)

    if not (File.Exists(projectFile)) then
        Error $"Project file not found: {projectFile}"
    else

        let sourceFiles = readSourceFilesFromFsproj projectFile
        let refs = resolveReferences projectFile

        if refs.Length = 0 then
            Error "Could not resolve project references — dotnet build may be unavailable offline"
        else

            let options = buildProjectOptions projectFile sourceFiles refs
            let checker = FSharpChecker.Create(keepAssemblyContents = true)

            let results = checker.ParseAndCheckProject(options) |> Async.RunSynchronously

            let projectSourceFiles = sourceFiles |> Array.map Path.GetFullPath |> Set.ofArray

            let inProject (entity: FSharpEntity) =
                let loc = Path.GetFullPath(entity.DeclarationLocation.FileName)
                Set.contains loc projectSourceFiles

            let types =
                results.AssemblySignature.Entities
                |> Seq.collect (collectFromEntity inProject)
                |> Seq.toList

            Ok types
