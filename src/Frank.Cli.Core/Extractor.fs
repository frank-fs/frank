module Frank.Cli.Core.Extractor

open System
open System.IO
open System.Diagnostics
open System.Xml.Linq
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

// ── type name normalization ───────────────────────────────────────────────────

let private normalizeTypeName (name: string) : string =
    name
    |> fun s -> s.Replace("Microsoft.FSharp.Core.", "")
    |> fun s -> s.Replace("Microsoft.FSharp.Collections.", "")
    |> fun s -> s.Replace("Microsoft.FSharp.Control.", "")

// ── attribute helpers ────────────────────────────────────────────────────────

let private attributeTypeName (attr: FSharpAttribute) =
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

let private buildAttributeMap (attrs: System.Collections.Generic.IList<FSharpAttribute>) : Map<string, string> =
    attrs
    |> Seq.map (fun a -> attributeTypeName a, firstConstructorArgAsString a)
    |> Map.ofSeq

// ── doc comment helper ───────────────────────────────────────────────────────

let private docComment (xmlDoc: FSharpXmlDoc) : string option =
    match xmlDoc with
    | FSharpXmlDoc.FromXmlText xt ->
        let lines = xt.UnprocessedLines

        if lines.Length = 0 then
            None
        else
            lines
            |> Array.map (fun s -> s.Trim())
            |> String.concat " "
            |> fun s -> if s = "" then None else Some s
    | _ -> None

// ── field extraction ─────────────────────────────────────────────────────────

let private mergeAttributeMaps (a: Map<string, string>) (b: Map<string, string>) : Map<string, string> =
    Map.fold (fun acc k v -> Map.add k v acc) a b

let private extractField (field: FSharpField) : FieldInfo =
    let attrs =
        mergeAttributeMaps (buildAttributeMap field.FieldAttributes) (buildAttributeMap field.PropertyAttributes)

    { Name = field.Name
      TypeName = field.FieldType.Format FSharpDisplayContext.Empty |> normalizeTypeName
      Attributes = attrs
      DocComment = docComment field.XmlDoc }

let private extractUnionCaseAsField (uc: FSharpUnionCase) : FieldInfo =
    let typeName =
        if uc.HasFields then
            uc.Fields
            |> Seq.map (fun f -> f.FieldType.Format FSharpDisplayContext.Empty |> normalizeTypeName)
            |> String.concat " * "
        else
            "unit"

    { Name = uc.Name
      TypeName = typeName
      Attributes = buildAttributeMap uc.Attributes
      DocComment = docComment uc.XmlDoc }

// ── entity → TypeInfo ────────────────────────────────────────────────────────

let private extractEntity (entity: FSharpEntity) : TypeInfo option =
    if entity.IsNamespace || entity.IsFSharpModule then
        None
    else
        let ns = entity.Namespace |> Option.defaultValue ""

        let fields =
            if entity.IsFSharpRecord then
                entity.FSharpFields |> Seq.map extractField |> Seq.toList
            elif entity.IsFSharpUnion then
                entity.UnionCases |> Seq.map extractUnionCaseAsField |> Seq.toList
            else
                []

        Some
            { FullName = entity.TryFullName |> Option.defaultValue entity.LogicalName
              Namespace = ns
              LocalName = entity.LogicalName
              Fields = fields
              Attributes = buildAttributeMap entity.Attributes
              DocComment = docComment entity.XmlDoc }

// ── recursive entity walk ────────────────────────────────────────────────────

let rec private collectEntities (projectSourceFiles: Set<string>) (entity: FSharpEntity) : TypeInfo list =
    let loc = Path.GetFullPath(entity.DeclarationLocation.FileName)
    let inProject = Set.contains loc projectSourceFiles

    let nested =
        entity.NestedEntities
        |> Seq.collect (collectEntities projectSourceFiles)
        |> Seq.toList

    if inProject then
        match extractEntity entity with
        | Some ti -> ti :: nested
        | None -> nested
    else
        nested

// ── project loading ───────────────────────────────────────────────────────────

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

/// Read Compile items from the .fsproj XML (relative paths resolved to absolute).
let private readSourceFilesFromFsproj (projectFile: string) : string[] =
    let dir = Path.GetDirectoryName(projectFile)
    let doc = XDocument.Load(projectFile)
    let ns = XNamespace.None

    doc.Descendants(XName.Get("Compile"))
    |> Seq.choose (fun el ->
        el.Attribute(XName.Get("Include"))
        |> Option.ofObj
        |> Option.map (fun a -> Path.GetFullPath(Path.Combine(dir, a.Value))))
    |> Seq.toArray

/// Get resolved reference DLLs by running `dotnet build` and capturing ReferencePath items.
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
                "build \"%s\" /p:TargetFramework=net8.0 /p:CustomAfterMicrosoftCommonTargets=\"%s\" /nologo /v:q"
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

// ── public API ────────────────────────────────────────────────────────────────

/// Extract TypeInfo records for all F# record and DU types defined in the project's
/// own source files (cross-project / NuGet types are excluded).
let extractTypeInfos (projectFile: string) : TypeInfo list =
    let projectFile = Path.GetFullPath(projectFile)

    if not (File.Exists(projectFile)) then
        invalidArg "projectFile" (sprintf "Project file not found: %s" projectFile)

    let sourceFiles = readSourceFilesFromFsproj projectFile
    let refs = resolveReferences projectFile
    let options = buildProjectOptions projectFile sourceFiles refs
    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    let results = checker.ParseAndCheckProject(options) |> Async.RunSynchronously

    let projectSourceFiles = sourceFiles |> Array.map Path.GetFullPath |> Set.ofArray

    results.AssemblySignature.Entities
    |> Seq.collect (collectEntities projectSourceFiles)
    |> Seq.toList
