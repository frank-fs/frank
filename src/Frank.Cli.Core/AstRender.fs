module Frank.Cli.Core.AstRender

open Fabulous.AST
open Fantomas.Core.SyntaxOak
open type Fabulous.AST.Ast

// ── Expression primitives (all yield WidgetBuilder<Expr>) ─────────────────────

/// A double-quoted string literal: "s"
let strExpr (s: string) : WidgetBuilder<Expr> = ConstantExpr(String s)

/// The bare identifier None
let noneExpr: WidgetBuilder<Expr> = ConstantExpr "None"

/// Some "<s>"
let someStrExpr (s: string) : WidgetBuilder<Expr> = AppExpr("Some", ConstantExpr(String s))

/// System.Uri applied to a string literal: Uri "<s>"
let uriExpr (s: string) : WidgetBuilder<Expr> = AppExpr("Uri", ConstantExpr(String s))

/// A record literal: { name1 = e1; name2 = e2; ... }
let recordExpr (fields: (string * WidgetBuilder<Expr>) list) : WidgetBuilder<Expr> =
    RecordExpr [ for (name, e) in fields -> RecordFieldExpr(name, e) ]

/// A list literal: [ e1; e2; ... ]
let listExpr (items: WidgetBuilder<Expr> list) : WidgetBuilder<Expr> = ListExpr items

// ── Module assembly ───────────────────────────────────────────────────────────

/// Split "A.B.Name" into ("A.B", "Name"); a dotless name becomes ("", name).
let private splitModuleName (moduleName: string) : string * string =
    match moduleName.LastIndexOf '.' with
    | -1 -> "", moduleName
    | i -> moduleName.[.. i - 1], moduleName.[i + 1 ..]

/// Render `namespace <ns>` + `module <name> = <opens> let <valueName>: <typeName> = <value>`.
/// Precondition: moduleName contains at least one '.' (RootNamespace-qualified, per the MSBuild targets).
let formatTypedValueModule
    (moduleName: string)
    (opens: string list)
    (valueName: string)
    (typeName: string)
    (value: WidgetBuilder<Expr>)
    : string =
    if moduleName.LastIndexOf '.' < 0 then
        invalidArg (nameof moduleName) "moduleName must be namespace-qualified (contain a '.')"

    let nsName, modName = splitModuleName moduleName

    let oak =
        Oak() {
            Namespace nsName {
                Module modName {
                    for o in opens do
                        Open o

                    Value(valueName, value, typeName)
                }
            }
        }

    oak |> Gen.mkOak |> Gen.run

// ── Union + match-function builders ──────────────────────────────────────────

/// A verbatim token expression (e.g. "typeof<T>"). Escape hatch for type applications.
let rawExpr (s: string) : WidgetBuilder<Expr> = ConstantExpr s

/// A function application: <func> <arg>, e.g. appExpr "System.Uri" (strExpr "x") → System.Uri "x"
let appExpr (func: string) (arg: WidgetBuilder<Expr>) : WidgetBuilder<Expr> = AppExpr(func, arg)

/// Wrapper so heterogeneous module declarations can be stored in a single list.
[<NoComparison; NoEquality>]
type ModuleDeclItem =
    | UnionDecl of WidgetBuilder<TypeDefnUnionNode>
    | BindingDecl of WidgetBuilder<BindingNode>

/// A discriminated-union type declaration: type <name> = | c1 | c2 ...
let unionDecl (name: string) (cases: string list) : ModuleDeclItem =
    UnionDecl(
        Union(name) {
            for c in cases do
                UnionCase(c)
        }
    )

/// let <name> (<paramName>: <paramType>) : <returnType> = match <paramName> with <clauses>
let matchFunction
    (name: string)
    (paramName: string)
    (paramType: string)
    (returnType: string)
    (clauses: (string * WidgetBuilder<Expr>) list)
    : ModuleDeclItem =
    let body =
        MatchExpr(paramName, [ for (pat, e) in clauses -> MatchClauseExpr(pat, e) ])

    BindingDecl(Function(name, [ ParenPat(ParameterPat(paramName, paramType)) ], body, returnType))

/// Render a module with multiple declarations and an optional leading comment.
/// Precondition: moduleName must be namespace-qualified (contain a '.').
let formatModule
    (moduleName: string)
    (leadingComment: string option)
    (opens: string list)
    (decls: ModuleDeclItem list)
    : string =
    if moduleName.LastIndexOf '.' < 0 then
        invalidArg (nameof moduleName) "moduleName must be namespace-qualified (contain a '.')"

    let i = moduleName.LastIndexOf '.'
    let nsName, modName = moduleName.[.. i - 1], moduleName.[i + 1 ..]

    let oak =
        Oak() {
            Namespace nsName {
                Module modName {
                    for o in opens do
                        Open o

                    for d in decls do
                        match d with
                        | UnionDecl u -> u
                        | BindingDecl b -> b
                }
            }
        }

    let body = oak |> Gen.mkOak |> Gen.run

    match leadingComment with
    | None -> body
    | Some c -> c + "\n" + body
