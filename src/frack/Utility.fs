#nowarn "77"
namespace Frack
module Utility =
  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key =
    ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value =
    ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg =
    ( ^a : (static member op_Implicit : ^b -> ^a) arg)