# Quickstart: smcat Native Annotations

## Using the Expanded SmcatMeta DU

### Pattern Matching on Annotations

```fsharp
open Frank.Statecharts.Ast

// Extract state type with fallback to default
let getStateType (annotations: Annotation list) =
    annotations
    |> List.tryPick (function
        | SmcatAnnotation(SmcatStateType(kind, origin)) -> Some(kind, origin)
        | _ -> None)
    |> Option.defaultValue (Regular, Inferred)

// Extract transition kind with fallback
let getTransitionKind (annotations: Annotation list) =
    annotations
    |> List.tryPick (function
        | SmcatAnnotation(SmcatTransition kind) -> Some kind
        | _ -> None)
    |> Option.defaultValue ExternalTransition

// Extract custom attributes (renamed from SmcatActivity)
let getCustomAttributes (annotations: Annotation list) =
    annotations
    |> List.choose (function
        | SmcatAnnotation(SmcatCustomAttribute(key, value)) -> Some(key, value)
        | _ -> None)
```

### Generator Output (before vs after)

**Before** (WSD-ported):
```fsharp
// State with Kind = Regular, no annotation — type info lost
StateDecl { Identifier = Some "initial"; Kind = Regular; Annotations = [] }
TransitionElement { Source = "initial"; Target = Some "Idle"; Annotations = [] }
```

**After** (smcat-native):
```fsharp
// State with typed Kind and annotation
StateDecl { Identifier = Some "initial"; Kind = Initial
            Annotations = [ SmcatAnnotation(SmcatStateType(Initial, Explicit)) ] }
TransitionElement { Source = "initial"; Target = Some "Idle"
                    Annotations = [ SmcatAnnotation(SmcatTransition InitialTransition) ] }
```

### Serializer Type Attribute Behavior

```fsharp
// Explicit origin → emits [type="initial"]
SmcatAnnotation(SmcatStateType(Initial, Explicit))
// serializes to: myState [type="initial"];

// Inferred origin → no type attribute
SmcatAnnotation(SmcatStateType(Initial, Inferred))
// serializes to: initial;  (name conveys the type)

// No annotation (Regular, Inferred default) → no type attribute
// serializes to: idle;
```

## Build and Test

```bash
# Build across all targets
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj

# Run smcat tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"

# Run all tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```
