module BaseSpecs
open NUnit.Framework

// NUnit helpers
let (==) (actual:#obj) (expected:#obj) = Assert.AreEqual(expected, actual)
let (!=) (actual:#obj) (expected:#obj) = Assert.AreNotEqual(expected, actual)
let (<->) (actual:#obj) expected = Assert.IsInstanceOf(expected, actual)
let (<!>) (actual:#obj) expected = Assert.IsNotInstanceOf(expected, actual)
let ``is null`` anObject = Assert.IsNull(anObject)
let ``is not null`` anObject = Assert.NotNull(anObject)
let ``is true`` claim = Assert.IsTrue(claim)
let ``is false`` claim = Assert.IsFalse(claim)