namespace HttpMachine
open System
open Cashel.Parser
open Cashel.ArraySegmentPrimitives

module Primitives =
  open Hex

  let ch = matchToken
  let str = matchTokens

  let token = any [ 0uy..127uy ]
  let upper = any [ 'A'B..'Z'B ]
  let lower = any [ 'a'B..'z'B ]
  let alpha = upper +++ lower
  let digit = any [ '0'B..'9'B ]
  let digitval = parse {
    let! d = digit
    return int d - int 48uy }
  let alphanum = alpha +++ digit
  let control = any [ 0uy..31uy ] +++ ch 127uy
  let tab = ch '\t'B
  let lf = ch '\n'B
  let cr = ch '\r'B
  let crlf = str (List.ofSeq "\r\n"B)
  let space = ch ' 'B
  let dquote = ch '"'B
  let hash = ch '#'B
  let percent = ch '%'B
  let plus = ch '+'B
  let hyphen = ch '-'B
  let dot = ch '.'B
  let colon = ch ':'B
  let slash = ch '/'B
  let qmark = ch '?'B
  let xupper = any [ 'A'B..'F'B ]
  let xlower = any [ 'a'B..'f'B ]
  let xchar = xupper +++ xlower
  let xdigit = digit +++ xchar
  let escaped = parse {
    do! forget percent
    let! d1 = xdigit
    let! d2 = xdigit
    let hex = fromHexDigit (char d1) <<< 4 ||| fromHexDigit (char d2)
    return byte hex }