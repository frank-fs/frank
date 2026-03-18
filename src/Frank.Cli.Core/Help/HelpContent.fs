namespace Frank.Cli.Core.Help

/// Hardcoded help metadata for all frank-cli commands and topics.
/// This is the primary documentation surface for LLM agents.
module HelpContent =

    let extractHelp: CommandHelp =
        { Name = "semantic extract"
          Summary = "Extract semantic definitions from F# source"
          Examples =
            [ { Invocation = "frank-cli semantic extract --project MyApp/MyApp.fsproj --base-uri http://example.org/"
                Description = "Extract semantic definitions from the MyApp project." }
              { Invocation = "frank-cli semantic extract --project MyApp/MyApp.fsproj --base-uri http://example.org/ --vocabularies schema.org,foaf"
                Description = "Extract with multiple vocabulary alignments." } ]
          Workflow =
            { StepNumber = 1
              Prerequisites = []
              NextSteps = [ "semantic clarify"; "semantic validate" ]
              IsOptional = false }
          Context =
            "The semantic extract command analyzes F# source code to derive an OWL ontology and SHACL shapes. It maps F# record types to OWL classes, record fields to OWL properties, and route definitions to resource identities. The extraction state is saved to obj/frank-cli/state.json for use by subsequent commands (clarify, validate, compile)." }

    let clarifyHelp: CommandHelp =
        { Name = "semantic clarify"
          Summary = "Identify ambiguities requiring human input"
          Examples =
            [ { Invocation = "frank-cli semantic clarify --project MyApp/MyApp.fsproj"
                Description = "Identify ambiguities in the extracted definitions." }
              { Invocation = "frank-cli semantic clarify --project MyApp/MyApp.fsproj --output-format json"
                Description = "Output ambiguities in JSON format for programmatic processing." } ]
          Workflow =
            { StepNumber = 2
              Prerequisites = [ "semantic extract" ]
              NextSteps = [ "semantic validate" ]
              IsOptional = true }
          Context =
            "The semantic clarify command identifies ambiguities in the extraction that require human judgment. For example, when a record field could map to multiple OWL properties, or when the relationship between types is unclear. Resolving clarifications improves the quality of the generated ontology and shapes." }

    let validateHelp: CommandHelp =
        { Name = "semantic validate"
          Summary = "Validate completeness and consistency of extracted definitions"
          Examples =
            [ { Invocation = "frank-cli semantic validate --project MyApp/MyApp.fsproj"
                Description = "Validate extracted definitions for completeness and consistency." }
              { Invocation = "frank-cli semantic validate --project MyApp/MyApp.fsproj --output-format json"
                Description = "Output validation results in JSON format." } ]
          Workflow =
            { StepNumber = 3
              Prerequisites = [ "semantic extract" ]
              NextSteps = [ "semantic compile" ]
              IsOptional = false }
          Context =
            "The semantic validate command checks the extracted semantic definitions for completeness and consistency. It verifies that all OWL classes have labels, that SHACL shapes reference valid classes, that property domains and ranges are consistent, and that the extraction is not stale relative to the source files." }

    let diffHelp: CommandHelp =
        { Name = "semantic diff"
          Summary = "Compare current extraction state with a previous snapshot"
          Examples =
            [ { Invocation = "frank-cli semantic diff --project MyApp/MyApp.fsproj"
                Description = "Compare current extraction state with the previous snapshot." }
              { Invocation = "frank-cli semantic diff --project MyApp/MyApp.fsproj --output-format json"
                Description = "Output differences in JSON format." } ]
          Workflow =
            { StepNumber = 4
              Prerequisites = [ "semantic extract" ]
              NextSteps = []
              IsOptional = true }
          Context =
            "The semantic diff command compares the current extraction state with a previously saved snapshot, showing what classes, properties, or shapes have been added, removed, or modified. This is useful for understanding the impact of source code changes on the semantic model." }

    let compileHelp: CommandHelp =
        { Name = "semantic compile"
          Summary = "Generate OWL/XML and SHACL artifacts from extraction state"
          Examples =
            [ { Invocation = "frank-cli semantic compile --project MyApp/MyApp.fsproj"
                Description = "Generate OWL/XML and SHACL artifacts from the current extraction state." }
              { Invocation = "frank-cli semantic compile --project MyApp/MyApp.fsproj --output-format json"
                Description = "Output compilation results in JSON format." } ]
          Workflow =
            { StepNumber = 5
              Prerequisites = [ "semantic extract" ]
              NextSteps = []
              IsOptional = false }
          Context =
            "The semantic compile command generates final artifact files from the extraction state: an OWL/XML ontology file (ontology.owl.xml), a SHACL shapes file (shapes.shacl.ttl), and a manifest file (manifest.json). These artifacts can be used by RDF tools, SPARQL endpoints, or other semantic web infrastructure." }

    let statechartExtractHelp: CommandHelp =
        { Name = "statechart extract"
          Summary = "Extract state machine metadata from F# source using the compiler"
          Examples =
            [ { Invocation = "frank-cli statechart extract --project MyApp/MyApp.fsproj"
                Description = "Extract all stateful resource metadata." }
              { Invocation = "frank-cli statechart extract --project MyApp/MyApp.fsproj --output-format json"
                Description = "Extract metadata in JSON format." } ]
          Workflow =
            { StepNumber = 1
              Prerequisites = []
              NextSteps = [ "statechart generate"; "statechart validate" ]
              IsOptional = false }
          Context =
            "Uses FSharp.Compiler.Service to analyze an F# project, finds statefulResource computation expressions, and extracts state machine metadata: state DU case names, initial state, allowed HTTP methods per state, and guard names. No assembly loading needed — works directly from source." }

    let statechartGenerateHelp: CommandHelp =
        { Name = "statechart generate"
          Summary = "Generate statechart spec artifacts from F# source"
          Examples =
            [ { Invocation = "frank-cli statechart generate --project MyApp/MyApp.fsproj --format wsd"
                Description = "Generate WSD notation for all stateful resources." }
              { Invocation = "frank-cli statechart generate --project MyApp/MyApp.fsproj --format all --output ./specs/"
                Description = "Generate all format artifacts and write to files." } ]
          Workflow =
            { StepNumber = 2
              Prerequisites = [ "statechart extract" ]
              NextSteps = [ "statechart validate" ]
              IsOptional = false }
          Context =
            "Analyzes F# source to extract state machine metadata and generates spec artifacts in the specified notation format. Supports WSD, ALPS, ALPS XML, SCXML, smcat, and XState JSON formats." }

    let statechartValidateHelp: CommandHelp =
        { Name = "statechart validate"
          Summary = "Validate statechart spec files for cross-format consistency"
          Examples =
            [ { Invocation = "frank-cli statechart validate game.wsd game.alps.json"
                Description = "Cross-format validation between WSD and ALPS specs." }
              { Invocation = "frank-cli statechart validate game.wsd game.scxml --output-format json"
                Description = "Cross-format validation with JSON output." } ]
          Workflow =
            { StepNumber = 3
              Prerequisites = [ "statechart generate" ]
              NextSteps = []
              IsOptional = false }
          Context =
            "Parses spec files and runs cross-format validation rules. Reports state/transition mismatches between spec files. Optionally accepts --assembly for code-truth extraction." }

    let statechartParseHelp: CommandHelp =
        { Name = "statechart parse"
          Summary = "Parse a spec file and output the StatechartDocument as JSON"
          Examples =
            [ { Invocation = "frank-cli statechart parse game.xstate.json"
                Description = "Parse an XState JSON file to StatechartDocument." }
              { Invocation = "frank-cli statechart parse game.json --format alps"
                Description = "Parse a .json file explicitly as ALPS format." } ]
          Workflow =
            { StepNumber = 1
              Prerequisites = []
              NextSteps = []
              IsOptional = true }
          Context =
            "Parses a spec file in any supported notation format and outputs the parsed StatechartDocument as JSON. Useful for LLM-assisted code scaffolding: the output can be consumed by code generation tools." }

    let statusHelp: CommandHelp =
        { Name = "status"
          Summary = "Show project extraction and compilation status"
          Examples =
            [ { Invocation = "frank-cli status --project MyApp/MyApp.fsproj"
                Description = "Show the current extraction and compilation status." }
              { Invocation = "frank-cli status --project MyApp/MyApp.fsproj --output-format json"
                Description = "Output status in JSON format for programmatic consumption." } ]
          Workflow =
            { StepNumber = 0
              Prerequisites = []
              NextSteps = []
              IsOptional = false }
          Context =
            "The status command inspects a project's extraction state directory (obj/frank-cli/) to determine what stage of the pipeline the project is in. It reports whether extraction has been performed, whether it is stale, whether compiled artifacts exist, and recommends the next action to take." }

    let helpHelp: CommandHelp =
        { Name = "help"
          Summary = "Show help topics and command documentation"
          Examples =
            [ { Invocation = "frank-cli help"
                Description = "List all available commands and topics." }
              { Invocation = "frank-cli help semantic extract"
                Description = "Show detailed help for the semantic extract command." }
              { Invocation = "frank-cli help statechart extract"
                Description = "Show detailed help for the statechart extract command." }
              { Invocation = "frank-cli help semantic-workflows"
                Description = "Show the semantic extraction pipeline guide." }
              { Invocation = "frank-cli help statechart-workflows"
                Description = "Show the statechart pipeline guide." } ]
          Workflow =
            { StepNumber = 0
              Prerequisites = []
              NextSteps = []
              IsOptional = false }
          Context =
            "The help command provides structured documentation for LLM agents and developers. It can display per-command help (including workflow position, examples, and context), topic guides (workflows, concepts), or a summary of all available commands and topics." }

    // -- Help Topics --

    let semanticWorkflowsTopic: HelpTopic =
        { Name = "semantic-workflows"
          Summary = "End-to-end guide to the semantic extraction pipeline"
          Content = """The frank-cli semantic extraction pipeline transforms F# source code into semantic
definitions (OWL ontology + SHACL shapes) through a series of commands:

  Step 1: semantic extract (required)
    Analyzes F# source code and produces initial semantic definitions.
    Prerequisites: (none)
    Next: semantic clarify, semantic validate

  Step 2: semantic clarify (optional)
    Identifies ambiguities in the extraction and presents questions.
    Prerequisites: semantic extract
    Next: semantic validate

  Step 3: semantic validate (required)
    Checks completeness and consistency of the extracted definitions.
    Prerequisites: semantic extract
    Next: semantic compile

  Step 4: semantic diff (optional)
    Compares current extraction state with a previous snapshot.
    Prerequisites: semantic extract
    Next: (informational only)

  Step 5: semantic compile (required)
    Generates final OWL/XML and SHACL artifact files.
    Prerequisites: semantic extract (validate recommended)
    Next: (end of pipeline)

Typical usage:
  frank-cli semantic extract --project MyApp.fsproj --base-uri http://example.org/
  frank-cli semantic clarify --project MyApp.fsproj
  frank-cli semantic validate --project MyApp.fsproj
  frank-cli semantic compile --project MyApp.fsproj""" }

    let statechartWorkflowsTopic: HelpTopic =
        { Name = "statechart-workflows"
          Summary = "End-to-end guide to the statechart pipeline"
          Content = """The statechart pipeline extracts, generates, validates, and parses
state machine artifacts from Frank applications:

  Step 1: statechart extract (required)
    Extracts state machine metadata from a compiled assembly.
    Prerequisites: (none)
    Next: statechart generate, statechart validate

  Step 2: statechart generate (required)
    Generates spec artifacts in notation formats (WSD, ALPS, SCXML, smcat, XState).
    Prerequisites: statechart extract
    Next: statechart validate

  Step 3: statechart validate (required)
    Validates spec files against compiled assembly code-truth.
    Prerequisites: statechart extract
    Next: (end of pipeline)

  statechart parse (standalone, optional)
    Parses a spec file and outputs the StatechartDocument as JSON.
    Prerequisites: (none)
    Use case: LLM-assisted code scaffolding from notation files.""" }

    let conceptsTopic: HelpTopic =
        { Name = "concepts"
          Summary = "Frank's semantic model: F# types, OWL, and SHACL"
          Content = """Frank bridges F# application code and semantic web standards. Understanding
the mapping between F# constructs and semantic artifacts helps you make
informed decisions during extraction and clarification.

  F# Record Types -> OWL Classes
    Each F# record type in your application is mapped to an OWL class in the
    generated ontology. The record name becomes the class URI (relative to
    your base URI). Record fields become OWL properties.

  Record Fields -> OWL Properties
    Each field in an F# record maps to an OWL property. The field's F# type
    determines the property's range: primitive types map to XSD datatypes,
    while record types create object properties linking to other classes.

  Route Definitions -> Resource Identities
    Frank resource definitions (routes) establish the identity of resources
    in your API. Each route pattern maps to a resource URI template, connecting
    your HTTP API surface to the semantic model.

  Type Constraints -> SHACL Shapes
    Constraints on F# types (such as required fields, option types, and
    collection types) are expressed as SHACL shapes. These shapes define
    validation rules that data must conform to.

  Vocabulary Alignment
    The extraction process can align your F# types with existing vocabularies
    (e.g., schema.org, FOAF). This maps your application-specific types to
    well-known semantic web terms, improving interoperability.""" }

    // -- Lookup Functions --

    /// Total number of steps in the semantic pipeline (for "Step N of M" display).
    let pipelineStepCount = 5

    /// All command help records.
    let allCommands: CommandHelp list =
        [ extractHelp
          clarifyHelp
          validateHelp
          diffHelp
          compileHelp
          statechartExtractHelp
          statechartGenerateHelp
          statechartValidateHelp
          statechartParseHelp
          statusHelp
          helpHelp ]

    /// All topic records.
    let allTopics: HelpTopic list =
        [ semanticWorkflowsTopic; statechartWorkflowsTopic; conceptsTopic ]

    /// Find a command by name (case-insensitive).
    let findCommand (name: string) : CommandHelp option =
        allCommands |> List.tryFind (fun c -> c.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))

    /// Find a topic by name (case-insensitive).
    let findTopic (name: string) : HelpTopic option =
        allTopics |> List.tryFind (fun t -> t.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))

    /// All known names (commands + topics) for fuzzy matching.
    let allNames: string list =
        (allCommands |> List.map (fun c -> c.Name)) @ (allTopics |> List.map (fun t -> t.Name))
