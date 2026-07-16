module Frank.Cli.Core.Clarify

open Frank.Semantic

val toJson: lf: LockFile.LockFile -> string

val toResolvedTemplate: lf: LockFile.LockFile -> string

val toMarkdown: lf: LockFile.LockFile -> string
