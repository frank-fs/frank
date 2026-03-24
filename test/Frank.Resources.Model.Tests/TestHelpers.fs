module Frank.Resources.Model.Tests.TestHelpers

open Frank.Resources.Model

let mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }
