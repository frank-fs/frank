namespace Fixtures

type PhoneRecord =
    { [<Pattern(@"^\+?[0-9]{7,15}$")>]
      PhoneNumber: string
      Name: string }

type UserRecord =
    { [<MinLength(3)>]
      Username: string }

type PostRecord =
    { [<MaxLength(280)>]
      Body: string }

type RatingRecord =
    { [<MinInclusive(1); MaxInclusive(10)>]
      Score: int }

type UsernameRecord =
    { [<MinLength(3); MaxLength(20); Pattern("^[a-z0-9_]+$")>]
      Username: string }
