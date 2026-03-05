module Fixtures.ComplexTypes

type Address = {
    Street: string
    City: string
    PostalCode: string
}

type Customer = {
    Id: System.Guid
    Name: string
    Email: string option
    Address: Address option
    Tags: string list
    CreatedAt: System.DateTime
}
