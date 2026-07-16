/// frank CLI entry point. All argument-parsing types (ClarifyArgs, ExtractArgs,
/// AcceptArgs, StatusArgs, FinalizeArgs, RefreshArgs, ValidateArgs, SemanticArgs,
/// FrankArgs) and command handlers are implementation details of this executable —
/// Frank.Cli.Tests exercises the tool as a subprocess, not by referencing these
/// types. Nothing here is a library API; the signature is deliberately empty (#392).
module Frank.Cli.Program
