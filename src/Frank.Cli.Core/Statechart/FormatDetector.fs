module Frank.Cli.Core.Statechart.FormatDetector

open Frank.Statecharts.Validation

/// Result of detecting a statechart format from a file path.
type DetectionResult =
    | Detected of FormatTag
    | Ambiguous of candidates: FormatTag list
    | Unsupported of extension: string

/// Detect the statechart format from a file path based on its extension.
/// Compound extensions (.alps.json, .xstate.json) are checked before plain .json.
let detect (filePath: string) : DetectionResult =
    let lower = filePath.ToLowerInvariant()

    if lower.EndsWith(".alps.json") then Detected Alps
    elif lower.EndsWith(".alps.xml") then Detected AlpsXml
    elif lower.EndsWith(".xstate.json") then Detected XState
    elif lower.EndsWith(".wsd") then Detected Wsd
    elif lower.EndsWith(".scxml") then Detected Scxml
    elif lower.EndsWith(".smcat") then Detected Smcat
    elif lower.EndsWith(".json") then Ambiguous [ Alps; XState ]
    elif lower.EndsWith(".xml") then Ambiguous [ AlpsXml; Scxml ]
    else Unsupported(System.IO.Path.GetExtension(filePath))

/// Get the canonical file extension for a format tag.
let formatExtension (tag: FormatTag) : string =
    match tag with
    | Wsd -> ".wsd"
    | Alps -> ".alps.json"
    | AlpsXml -> ".alps.xml"
    | Scxml -> ".scxml"
    | Smcat -> ".smcat"
    | XState -> ".xstate.json"

/// Human-readable list of supported format extensions.
let supportedFormats : string =
    ".wsd, .alps.json, .alps.xml, .scxml, .smcat, .xstate.json"

module FormatTag =
    let toString (tag: FormatTag) : string =
        match tag with
        | Wsd -> "WSD"
        | Alps -> "ALPS"
        | AlpsXml -> "ALPS XML"
        | Scxml -> "SCXML"
        | Smcat -> "smcat"
        | XState -> "XState"

    let toLower (tag: FormatTag) : string =
        match tag with
        | Wsd -> "wsd"
        | Alps -> "alps"
        | AlpsXml -> "alps-xml"
        | Scxml -> "scxml"
        | Smcat -> "smcat"
        | XState -> "xstate"
