namespace Frank.Cli.Core.Help

/// Resolution logic for the `frank-cli help <arg>` subcommand.
/// Resolves arguments to commands, topics, or fuzzy suggestions.
module HelpSubcommand =

    /// Maximum Levenshtein distance for fuzzy suggestions.
    let private maxSuggestionDistance = 3

    /// Resolve a help argument to a command, topic, or suggestion list.
    /// Priority order: exact command match > exact topic match > fuzzy suggestions.
    /// Matching is case-insensitive (handled by HelpContent.findCommand / findTopic).
    let resolve (argument: string) : HelpLookupResult =
        // 1. Check commands first (commands take priority per spec edge case)
        match HelpContent.findCommand argument with
        | Some cmd -> CommandMatch cmd
        | None ->
            // 2. Check topics
            match HelpContent.findTopic argument with
            | Some topic -> TopicMatch topic
            | None ->
                // 3. No match -- provide fuzzy suggestions
                let suggestions =
                    FuzzyMatch.suggest argument HelpContent.allNames maxSuggestionDistance
                    |> List.map fst // Extract just the names, drop distances
                NoMatch suggestions

    /// Result type for the no-argument help index.
    type HelpIndex =
        { /// (name, summary) pairs for all registered commands
          Commands: (string * string) list
          /// (name, summary) pairs for all registered topics
          Topics: (string * string) list }

    /// List all commands and topics for the help index display.
    /// Called when `frank-cli help` is invoked with no argument.
    let listAll () : HelpIndex =
        { Commands =
            HelpContent.allCommands
            |> List.map (fun c -> (c.Name, c.Summary))
          Topics =
            HelpContent.allTopics
            |> List.map (fun t -> (t.Name, t.Summary)) }
