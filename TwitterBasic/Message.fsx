#r "nuget: Akka.Serialization.Hyperion"
open Akka.Serialization


type Message =
    | Register of string * string * string * string
    | Send of string * string * string * string * bool
    | Subscribe of string * string * string * string
    | Retweet of string * string * string * string
    | Hashtag of string * string
    | Mention of string * string
    | Tweets of string * string * string
    | ClientRequest of string * string * string * string * string * string * string * string * string
