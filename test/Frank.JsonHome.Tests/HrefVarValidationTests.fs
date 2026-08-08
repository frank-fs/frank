module Frank.JsonHome.Tests.HrefVarValidationTests

open Expecto
open Frank.JsonHome

[<Tests>]
let tests =
    testList
        "HrefVarValidation.diff"
        [ test "no mismatch when declared names match template variables exactly" {
              let result = HrefVarValidation.diff "/products/{id}" [ "id" ]
              Expect.isTrue (HrefVarValidation.isValid result) "expected no mismatch"
          }

          test "flags a declared name with no matching template variable" {
              let result = HrefVarValidation.diff "/products/{id}" [ "prodId" ]
              Expect.equal result.Extra [ "prodId" ] "extra"
              Expect.equal result.Missing [ "id" ] "missing"
          }

          test "flags a template variable with no declaration" {
              let result = HrefVarValidation.diff "/products/{id}" []
              Expect.equal result.Missing [ "id" ] "missing"
              Expect.isEmpty result.Extra "no extras"
          }

          test "a non-templated route flags every declared name as extra" {
              let result = HrefVarValidation.diff "/products" [ "id" ]
              Expect.equal result.Extra [ "id" ] "extra"
              Expect.isEmpty result.Missing "no missing"
          }

          test "a repeated template variable is not double-counted" {
              let result = HrefVarValidation.diff "/a/{id}/b/{id}" [ "id" ]
              Expect.isTrue (HrefVarValidation.isValid result) "expected no mismatch"
          }

          // Held out deliberately: no other task in this plan uses this
          // template/name pair. A `diff` implemented as a lookup table over
          // the literal tuples used elsewhere (T001/T004/T007 all reuse
          // "/products/{id}" / "id" / "prodId") cannot pass this case.
          test "a distinct template/name pair not reused elsewhere in this plan" {
              let result = HrefVarValidation.diff "/orders/{orderId}/items/{itemId}" [ "orderId" ]
              Expect.equal result.Missing [ "itemId" ] "missing"
              Expect.isEmpty result.Extra "no extras"
          }

          // Both directions in the same diff call -- T004/T007's fixtures
          // never combine Missing and Extra in one resource; this does.
          test "missing and extra reported together in one diff" {
              let result = HrefVarValidation.diff "/a/{x}/{y}" [ "x"; "z" ]
              Expect.equal result.Missing [ "y" ] "missing"
              Expect.equal result.Extra [ "z" ] "extra"
          } ]
