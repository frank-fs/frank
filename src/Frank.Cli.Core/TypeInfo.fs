namespace Frank.Cli.Core

type FieldInfo =
    { Name: string
      TypeName: string
      Attributes: Map<string, string>
      DocComment: string option }

type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Fields: FieldInfo list
      Attributes: Map<string, string>
      DocComment: string option }
