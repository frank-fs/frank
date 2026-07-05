module Frank.TestSupport.TempDir

open System.IO

/// On macOS /var is a symlink to /private/var. MSBuild resolves the real path when
/// computing relative ProjectReference paths. Canonicalize so temp dirs match what
/// MSBuild sees, allowing genuine ProjectReferences.
let private canonicalizeTempDir (dir: string) : string =
    let varTarget = DirectoryInfo("/var").ResolveLinkTarget(returnFinalTarget = true)

    if varTarget <> null && dir.StartsWith("/var/") then
        Path.Combine(varTarget.FullName, dir.[5..])
    else
        dir

/// Creates a fresh random temp directory (symlink-canonicalized), runs f, then recursively deletes it.
let withTempDir (f: string -> 'a) : 'a =
    let rawDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let dir = canonicalizeTempDir rawDir
    Directory.CreateDirectory dir |> ignore

    try
        f dir
    finally
        Directory.Delete(dir, recursive = true)
