module Frank.Statecharts.Tests.Alps.DualityTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Resources.Model

// ---------------------------------------------------------------------------
// Order Fulfillment golden file with duality extensions
// ---------------------------------------------------------------------------

/// 4-role order fulfillment ALPS document with duality annotations.
/// Exercises: asymmetric roles, observer-only role, multi-role advancing state,
/// cut points, and observer-to-actor transitions.
let orderFulfillmentAlpsJson =
    """{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "Order fulfillment protocol" },
    "ext": [
      { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Buyer,Seller,Warehouse,Auditor" }
    ],
    "descriptor": [
      {
        "id": "orderId",
        "type": "semantic",
        "doc": { "format": "text", "value": "Order identifier" }
      },
      {
        "id": "Submitted",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Order submitted, awaiting seller confirmation" },
        "ext": [
          { "id": "https://frank-fs.github.io/alps-ext/clientObligation", "value": "may-poll" }
        ],
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Submitted"
          },
          {
            "id": "confirmOrder",
            "type": "unsafe",
            "rt": "#Confirmed",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "SellerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Seller" },
              { "id": "https://frank-fs.github.io/alps-ext/advancesProtocol", "value": "true" }
            ]
          },
          {
            "id": "rejectOrder",
            "type": "unsafe",
            "rt": "#Cancelled",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "SellerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Seller" }
            ]
          }
        ]
      },
      {
        "id": "Confirmed",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Order confirmed by seller" },
        "ext": [
          { "id": "https://frank-fs.github.io/alps-ext/clientObligation", "value": "must-select" }
        ],
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Confirmed"
          },
          {
            "id": "submitPayment",
            "type": "unsafe",
            "rt": "#Paid",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "BuyerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Buyer" },
              { "id": "https://frank-fs.github.io/alps-ext/advancesProtocol", "value": "true" },
              { "id": "https://frank-fs.github.io/alps-ext/dualOf", "value": "#confirmOrder" }
            ]
          },
          {
            "id": "cancelOrder",
            "type": "unsafe",
            "rt": "#Cancelled",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "BuyerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Buyer" }
            ]
          },
          {
            "id": "cancelBySeller",
            "type": "unsafe",
            "rt": "#Cancelled",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "SellerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Seller" }
            ]
          }
        ]
      },
      {
        "id": "Paid",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Payment received" },
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Paid"
          },
          {
            "id": "beginPicking",
            "type": "unsafe",
            "rt": "#Picking",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "WarehouseGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Warehouse" },
              { "id": "https://frank-fs.github.io/alps-ext/cutPoint", "value": "service-b#acceptOrder" },
              { "id": "https://frank-fs.github.io/alps-ext/advancesProtocol", "value": "true" }
            ]
          }
        ]
      },
      {
        "id": "Picking",
        "type": "semantic",
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Picking"
          },
          {
            "id": "shipOrder",
            "type": "unsafe",
            "rt": "#Shipped",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "WarehouseGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Warehouse" }
            ]
          }
        ]
      },
      {
        "id": "Shipped",
        "type": "semantic",
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Shipped"
          },
          {
            "id": "confirmDelivery",
            "type": "unsafe",
            "rt": "#Completed",
            "ext": [
              { "id": "https://frank-fs.github.io/alps-ext/guard", "value": "SellerGuard" },
              { "id": "https://frank-fs.github.io/alps-ext/projectedRole", "value": "Seller" }
            ]
          }
        ]
      },
      {
        "id": "Completed",
        "type": "semantic",
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Completed"
          }
        ]
      },
      {
        "id": "Cancelled",
        "type": "semantic",
        "descriptor": [
          {
            "id": "viewOrder",
            "type": "safe",
            "rt": "#Cancelled"
          }
        ]
      }
    ]
  }
}"""

/// Helper: parse and get document (assert no errors).
let private parseOk json msg =
    let result = parseAlpsJson json
    Expect.isEmpty result.Errors msg
    result.Document

/// Extract all TransitionEdges from a StatechartDocument's elements.
let private getTransitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract all StateNodes from a StatechartDocument's elements.
let private getStates (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Collect all AlpsDuality annotations from all elements.
let private collectDualityAnnotations (doc: StatechartDocument) =
    let fromAnnotations anns =
        anns
        |> List.choose (fun a ->
            match a with
            | AlpsAnnotation(AlpsDuality(kind, value)) -> Some(kind, value)
            | _ -> None)

    let stateAnns = getStates doc |> List.collect (fun s -> fromAnnotations s.Annotations)
    let transAnns = getTransitions doc |> List.collect (fun t -> fromAnnotations t.Annotations)
    stateAnns @ transAnns

// ---------------------------------------------------------------------------
// Part 1: ALPS Duality Round-trip Tests
// ---------------------------------------------------------------------------

[<Tests>]
let dualityRoundTripTests =
    testList
        "Alps.Duality.RoundTrip"
        [ testCase "order fulfillment JSON roundtrip preserves all information"
          <| fun _ ->
              let original = parseOk orderFulfillmentAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"
              Expect.equal roundTripped original "roundtrip preserves all information"

          testCase "cutPoint on beginPicking roundtrips correctly"
          <| fun _ ->
              let original = parseOk orderFulfillmentAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"

              let beginPicking =
                  getTransitions roundTripped
                  |> List.find (fun t -> t.Event = Some "beginPicking")

              let cutPoints =
                  beginPicking.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDuality(CutPoint, value)) -> Some value
                      | _ -> None)

              Expect.hasLength cutPoints 1 "beginPicking has one cutPoint annotation"
              Expect.equal cutPoints.[0] "service-b#acceptOrder" "cutPoint value preserved"

          testCase "clientObligation annotations vary by state"
          <| fun _ ->
              let doc = parseOk orderFulfillmentAlpsJson "parse failed"

              let stateObligation stateId =
                  getStates doc
                  |> List.find (fun s -> s.Identifier = Some stateId)
                  |> fun s ->
                      s.Annotations
                      |> List.tryPick (fun a ->
                          match a with
                          | AlpsAnnotation(AlpsDuality(ClientObligation, v)) -> Some v
                          | _ -> None)

              Expect.equal (stateObligation "Submitted") (Some "may-poll") "Submitted has may-poll obligation"
              Expect.equal (stateObligation "Confirmed") (Some "must-select") "Confirmed has must-select obligation"

          testCase "advancesProtocol annotations roundtrip on transitions"
          <| fun _ ->
              let original = parseOk orderFulfillmentAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"

              let advancingTransitions =
                  getTransitions roundTripped
                  |> List.filter (fun t ->
                      t.Annotations
                      |> List.exists (fun a ->
                          match a with
                          | AlpsAnnotation(AlpsDuality(AdvancesProtocol, "true")) -> true
                          | _ -> false))

              let names = advancingTransitions |> List.choose (fun t -> t.Event) |> Set.ofList

              Expect.contains names "confirmOrder" "confirmOrder advances protocol"
              Expect.contains names "submitPayment" "submitPayment advances protocol"
              Expect.contains names "beginPicking" "beginPicking advances protocol"

          testCase "dualOf annotation roundtrips on submitPayment"
          <| fun _ ->
              let original = parseOk orderFulfillmentAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"

              let submitPayment =
                  getTransitions roundTripped
                  |> List.find (fun t -> t.Event = Some "submitPayment")

              let dualOfValues =
                  submitPayment.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDuality(DualOf, value)) -> Some value
                      | _ -> None)

              Expect.hasLength dualOfValues 1 "submitPayment has one dualOf annotation"
              Expect.equal dualOfValues.[0] "#confirmOrder" "dualOf references confirmOrder"

          testCase "all duality annotations preserved in roundtrip"
          <| fun _ ->
              let original = parseOk orderFulfillmentAlpsJson "parse failed"
              let generated = generateAlpsJson original
              let roundTripped = parseOk generated "re-parse failed"

              let originalDuality = collectDualityAnnotations original
              let roundTrippedDuality = collectDualityAnnotations roundTripped

              Expect.equal roundTrippedDuality originalDuality "all duality annotations preserved"

          testCase "4-role projection produces structurally distinct per-role results"
          <| fun _ ->
              let doc = parseOk orderFulfillmentAlpsJson "parse failed"
              let transitions = Frank.Statecharts.TransitionExtractor.extract doc
              let roles = Frank.Statecharts.TransitionExtractor.extractRoles doc

              let chart: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames =
                      [ "Submitted"
                        "Confirmed"
                        "Paid"
                        "Picking"
                        "Shipped"
                        "Completed"
                        "Cancelled" ]
                    InitialStateKey = "Submitted"
                    GuardNames = [ "SellerGuard"; "BuyerGuard"; "WarehouseGuard" ]
                    StateMetadata =
                      Map.ofList
                          [ "Submitted",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Confirmed",
                            { AllowedMethods = [ "GET"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "Paid",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Picking",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Shipped",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Completed",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None }
                            "Cancelled",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = roles
                    Transitions = transitions }

              let projections = Projection.projectAll chart

              Expect.hasLength (projections |> Map.toList) 4 "4 role projections"

              // Buyer has: viewOrder (all), submitPayment, cancelOrder
              let buyerTransitions =
                  projections.["Buyer"].Transitions |> List.map (fun t -> t.Event) |> Set.ofList

              Expect.contains buyerTransitions "submitPayment" "Buyer can submit payment"
              Expect.contains buyerTransitions "cancelOrder" "Buyer can cancel order"
              Expect.isFalse (Set.contains "confirmOrder" buyerTransitions) "Buyer cannot confirm"

              // Seller has: viewOrder (all), confirmOrder, rejectOrder, cancelBySeller, confirmDelivery
              let sellerTransitions =
                  projections.["Seller"].Transitions |> List.map (fun t -> t.Event) |> Set.ofList

              Expect.contains sellerTransitions "confirmOrder" "Seller can confirm"
              Expect.contains sellerTransitions "rejectOrder" "Seller can reject"
              Expect.contains sellerTransitions "confirmDelivery" "Seller can confirm delivery"

              // Warehouse has: viewOrder (all), beginPicking, shipOrder
              let warehouseTransitions =
                  projections.["Warehouse"].Transitions |> List.map (fun t -> t.Event) |> Set.ofList

              Expect.contains warehouseTransitions "beginPicking" "Warehouse can begin picking"
              Expect.contains warehouseTransitions "shipOrder" "Warehouse can ship"
              Expect.isFalse (Set.contains "confirmOrder" warehouseTransitions) "Warehouse cannot confirm"

              // Auditor has: viewOrder only (all unrestricted transitions)
              let auditorTransitions =
                  projections.["Auditor"].Transitions |> List.map (fun t -> t.Event) |> Set.ofList

              Expect.equal auditorTransitions (Set.singleton "viewOrder") "Auditor can only view" ]
