namespace Fixtures

open System

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type PatternAttribute(regex: string) =
    inherit Attribute()
    member _.Regex = regex

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type MinInclusiveAttribute(value: int) =
    inherit Attribute()
    member _.Value = value

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type MaxInclusiveAttribute(value: int) =
    inherit Attribute()
    member _.Value = value

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type MinLengthAttribute(length: int) =
    inherit Attribute()
    member _.Length = length

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type MaxLengthAttribute(length: int) =
    inherit Attribute()
    member _.Length = length
