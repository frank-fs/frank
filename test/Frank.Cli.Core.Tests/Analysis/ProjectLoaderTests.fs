module Frank.Cli.Core.Tests.Analysis.ProjectLoaderTests

open System
open System.IO
open System.Text
open Expecto
open Frank.Cli.Core.Analysis

let private fixturesPath () =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

/// Write a temp JSON file, run f with its path, then delete the file.
let private withTempJson (content: string) (f: string -> 'a) =
    let path = Path.GetTempFileName()

    try
        File.WriteAllText(path, content)
        f path
    finally
        if File.Exists path then
            File.Delete path

[<Tests>]
let tests =
    testList
        "ProjectLoader"
        [
          // ── loadProject ────────────────────────────────────────────────────────

          testCaseAsync "non-existent project returns Error"
          <| async {
              let! result = ProjectLoader.loadProject "/nonexistent/path/project.fsproj"

              match result with
              | Error msg -> Expect.stringContains msg "not found" "Should indicate file not found"
              | Ok _ -> failwith "Should return Error for non-existent project"
          }

          testCaseAsync "loads real F# project and returns source files"
          <| async {
              let! result = ProjectLoader.loadProject (fixturesPath ())

              match result with
              | Error e -> failwith $"Should load successfully: {e}"
              | Ok loaded ->
                  Expect.isGreaterThan loaded.ParsedFiles.Length 0 "Should have parsed files"

                  let fileNames =
                      loaded.ParsedFiles |> List.map (fun (path, _) -> Path.GetFileName path)

                  Expect.contains fileNames "SimpleTypes.fs" "Should contain SimpleTypes.fs"
                  Expect.contains fileNames "ConstraintAttributes.fs" "Should contain ConstraintAttributes.fs"
          }

          testCaseAsync "loaded project has type-check results"
          <| async {
              let! result = ProjectLoader.loadProject (fixturesPath ())

              match result with
              | Error e -> failwith $"Should load successfully: {e}"
              | Ok loaded ->
                  Expect.isFalse loaded.CheckResults.HasCriticalErrors "Should have no critical errors"
                  let entities = loaded.CheckResults.AssemblySignature.Entities |> Seq.length
                  Expect.isGreaterThan entities 0 "Should have type entities"
          }

          // ── readResolvedOptions ────────────────────────────────────────────────

          testCase "readResolvedOptions with valid JSON returns Ok"
          <| fun _ ->
              let json =
                  """{"sourceFiles":["/tmp/A.fs"],"references":["/tmp/System.Runtime.dll"],"defines":["DEBUG"],"otherFlags":["--langversion:9.0"]}"""

              let result = withTempJson json ProjectLoader.readResolvedOptions

              match result with
              | Error e -> failwith $"Expected Ok, got Error: {e}"
              | Ok opts ->
                  Expect.equal opts.SourceFiles [ "/tmp/A.fs" ] "SourceFiles should match"
                  Expect.equal opts.References [ "/tmp/System.Runtime.dll" ] "References should match"
                  Expect.equal opts.Defines [ "DEBUG" ] "Defines should match"
                  Expect.equal opts.OtherFlags [ "--langversion:9.0" ] "OtherFlags should match"

          testCase "readResolvedOptions with empty references returns Error"
          <| fun _ ->
              let json =
                  """{"sourceFiles":["/tmp/A.fs"],"references":[],"defines":[],"otherFlags":[]}"""

              let result = withTempJson json ProjectLoader.readResolvedOptions

              match result with
              | Error msg ->
                  Expect.stringContains msg "No assembly references" "Error should mention missing references"
              | Ok _ -> failwith "Expected Error for empty references"

          testCase "readResolvedOptions with malformed JSON returns Error"
          <| fun _ ->
              let result = withTempJson "{not valid json" ProjectLoader.readResolvedOptions

              match result with
              | Error msg ->
                  Expect.stringContains msg "Failed to parse project options file" "Error should describe parse failure"
              | Ok _ -> failwith "Expected Error for malformed JSON"

          testCase "readResolvedOptions with non-existent file returns Error"
          <| fun _ ->
              let result = ProjectLoader.readResolvedOptions "/nonexistent/path/options.json"

              match result with
              | Error msg -> Expect.stringContains msg "not found" "Error should indicate file not found"
              | Ok _ -> failwith "Expected Error for non-existent file"

          testCase "readResolvedOptions with BOM-prefixed file parses correctly"
          <| fun _ ->
              let json =
                  """{"sourceFiles":["/tmp/A.fs"],"references":["/tmp/mscorlib.dll"],"defines":[],"otherFlags":[]}"""

              let path = Path.GetTempFileName()

              try
                  // Write with UTF-8 BOM, simulating a file written by some MSBuild tasks
                  File.WriteAllText(path, json, Encoding.UTF8)
                  // File.ReadAllText with no encoding arg strips BOM on .NET 6+
                  let result = ProjectLoader.readResolvedOptions path

                  match result with
                  | Error e -> failwith $"Expected Ok with BOM file, got Error: {e}"
                  | Ok opts ->
                      Expect.equal opts.SourceFiles [ "/tmp/A.fs" ] "SourceFiles should match after BOM strip"
                      Expect.equal opts.References [ "/tmp/mscorlib.dll" ] "References should match after BOM strip"
              finally
                  if File.Exists path then
                      File.Delete path

          // ── loadProjectFromOptions ────────────────────────────────────────────

          testCaseAsync "loadProjectFromOptions with empty source files returns Error"
          <| async {
              let opts =
                  { SourceFiles = []
                    References = [ "/tmp/System.Runtime.dll" ]
                    Defines = []
                    OtherFlags = [] }

              let! result = ProjectLoader.loadProjectFromOptions "/tmp/fake.fsproj" opts

              match result with
              | Error msg -> Expect.stringContains msg "No source files" "Should describe missing source files"
              | Ok _ -> failwith "Expected Error for empty source files"
          }

          testCaseAsync "loadProjectFromOptions against Fixtures.fsproj succeeds"
          <| async {
              // First resolve options via the standard path so we have real references.
              let! loadResult = ProjectLoader.loadProject (fixturesPath ())

              match loadResult with
              | Error e -> failwith $"loadProject prerequisite failed: {e}"
              | Ok standard ->
                  // Build ResolvedProjectOptions from what loadProject resolved.
                  let sourceFiles = standard.ParsedFiles |> List.map fst
                  // Extract references from the FCS project options command line args.
                  // They appear as -r:<path> entries.  We reconstruct them from the
                  // check results diagnostics access rather than re-invoking MSBuild.
                  // For a lighter approach: use the assembly refs from the AssemblySignature.
                  // However the simplest and most reliable approach for this test is to use
                  // loadProjectFromOptions with a small valid options set that just covers
                  // the fixture source files. We accept that references need real paths,
                  // so we obtain them via a second loadProject call in parallel.

                  // We already have real source files from `standard`; build opts
                  // directly. For references, note that `standard.CheckResults` exposes
                  // referenced assemblies. We use that.
                  let refs =
                      standard.CheckResults.ProjectContext.GetReferencedAssemblies()
                      |> List.choose (fun asm -> asm.FileName)

                  let opts =
                      { SourceFiles = sourceFiles
                        References = refs
                        Defines = []
                        OtherFlags = [] }

                  let! result = ProjectLoader.loadProjectFromOptions (fixturesPath ()) opts

                  match result with
                  | Error e -> failwith $"loadProjectFromOptions should succeed: {e}"
                  | Ok loaded ->
                      Expect.isGreaterThan loaded.ParsedFiles.Length 0 "Should have parsed files"

                      let fileNames =
                          loaded.ParsedFiles |> List.map (fun (path, _) -> Path.GetFileName path)

                      Expect.contains fileNames "SimpleTypes.fs" "Should contain SimpleTypes.fs"
          } ]
