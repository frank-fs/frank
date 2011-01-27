module HexSpecs
open HttpMachine.Hex
open NUnit.Framework
open BaseSpecs

[<TestCase(0, '0')>]
[<TestCase(1, '1')>]
[<TestCase(2, '2')>]
[<TestCase(3, '3')>]
[<TestCase(4, '4')>]
[<TestCase(5, '5')>]
[<TestCase(6, '6')>]
[<TestCase(7, '7')>]
[<TestCase(8, '8')>]
[<TestCase(9, '9')>]
[<TestCase(10, 'A')>]
[<TestCase(11, 'B')>]
[<TestCase(12, 'C')>]
[<TestCase(13, 'D')>]
[<TestCase(14, 'E')>]
[<TestCase(15, 'F')>]
let ``test should convert a hex digit to a char``(x, expected) =
  toHexDigit x == expected

[<TestCase('0', 0)>]
[<TestCase('1', 1)>]
[<TestCase('2', 2)>]
[<TestCase('3', 3)>]
[<TestCase('4', 4)>]
[<TestCase('5', 5)>]
[<TestCase('6', 6)>]
[<TestCase('7', 7)>]
[<TestCase('8', 8)>]
[<TestCase('9', 9)>]
[<TestCase('A', 10)>]
[<TestCase('B', 11)>]
[<TestCase('C', 12)>]
[<TestCase('D', 13)>]
[<TestCase('E', 14)>]
[<TestCase('F', 15)>]
[<TestCase('a', 10)>]
[<TestCase('b', 11)>]
[<TestCase('c', 12)>]
[<TestCase('d', 13)>]
[<TestCase('e', 14)>]
[<TestCase('f', 15)>]
let ``test should convert a char to a hex digit``(chr, expected) =
  fromHexDigit chr == expected

[<TestCase("20", 32uy)>]
[<TestCase("2F", 47uy)>]
[<TestCase("2f", 47uy)>]
let ``test should decode a hexadecimal string into an integer``(input, expected) =
  (decode input).[0] == expected