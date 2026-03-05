module Fixtures.SimpleTypes

type Status = Active | Inactive

type Product = {
    Id: int
    Name: string
    IsAvailable: bool
}
