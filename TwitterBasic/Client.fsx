#r "nuget: Akka"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.TestKit"
#load "Message.fsx"

open System
open System.Threading
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open Message

let args: string array = fsi.CommandLineArgs |> Array.tail
let N = args.[0] |> int
let M = N
let mutable i = 0
let mutable ii = 0
let obj = new Object()

let addIIByOne () =
    Monitor.Enter obj
    ii <- ii + 1
    Monitor.Exit obj

let config =
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : DEBUG
            loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
                serializers {
                   hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    }
                    serialization-bindings {
                                           ""System.Object"" = hyperion
                    }
            }
            remote {
                helios.tcp {
                    port = 8123
                    hostname = localhost
                }
            }
        }"
    )

let system = ActorSystem.Create("Client", config)

let echoServer =
    system.ActorSelection("akka.tcp://Server@localhost:8777/user/RouterServer")

let rand = System.Random(1)

let actorRegisterUser (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let idx = message
            let mutable opt = "reg"
            let mutable POST = " "
            let mutable username = "user" + (string idx)
            let mutable password = "password" + (string idx)
            let mutable targetUsername = " "
            let mutable queryhashtag = " "
            let mutable at = " "
            let mutable tweetContent = " "
            let mutable register = " "

            let msg =
                ClientRequest(opt, "post", username, password, targetUsername, tweetContent, queryhashtag, at, register)

            let cmd =
                opt
                + ","
                + POST
                + ","
                + username
                + ","
                + password
                + ","
                + targetUsername
                + ","
                + tweetContent
                + ","
                + queryhashtag
                + ","
                + at
                + ","
                + register

            let task = echoServer <? msg
            let response = Async.RunSynchronously(task, 1000)

            addIIByOne ()
            return! loop ()
        }

    loop ()

let actorClientSimulator (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()
            let idx = message

            match box message with
            | :? string ->
                let mutable randnum = Random().Next() % 7
                let mutable opt = "reg"
                let mutable post = "POST"
                let mutable username = "user" + (string idx)
                let mutable password = "password" + (string idx)
                let mutable targetUsername = "user" + rand.Next(N).ToString()
                let mutable queryhashtag = "#topic" + rand.Next(N).ToString()
                let mutable at = "@user" + rand.Next(N).ToString()

                let mutable tweetcontent =
                    "tweet"
                    + rand.Next(N).ToString()
                    + "... "
                    + queryhashtag
                    + "..."
                    + at
                    + " "

                let mutable register = "register"
                if randnum = 0 then opt <- "reg"
                if randnum = 1 then opt <- "send"
                if randnum = 2 then opt <- "subscribe"
                if randnum = 3 then opt <- "retweet"
                if randnum = 4 then opt <- "querying"
                if randnum = 5 then opt <- "#"
                if randnum = 6 then opt <- "@"


                let msg =
                    ClientRequest(
                        opt,
                        "POST",
                        username,
                        password,
                        targetUsername,
                        tweetcontent,
                        queryhashtag,
                        at,
                        register
                    )

                printfn "Client simulator"
                let task = echoServer <? msg
                let response = Async.RunSynchronously(task, 3000)

                addIIByOne ()

            return! loop ()
        }

    loop ()

let clientUserRegistration =
    spawn system "userRegistration" actorRegisterUser

let clientSimulation =
    spawn system "simulator" actorClientSimulator

printfn "Registering accounts...."
let stopWatch = System.Diagnostics.Stopwatch.StartNew()
i <- 0
ii <- 0

while i < N do
    clientUserRegistration <! string i |> ignore
    i <- i + 1

while ii < N - 1 do
    Thread.Sleep(50)

stopWatch.Stop()
let registerTime = stopWatch.Elapsed.TotalMilliseconds



printfn "Sending tweets...."
stopWatch = System.Diagnostics.Stopwatch.StartNew()

for i in 0 .. N - 1 do
    for j in 0 .. 10 do
        let cmd =
            "send, ,user"
            + (string i)
            + ",password"
            + (string i)
            + ", ,tweet+user"
            + (string i)
            + "_"
            + (string j)
            + "th @user"
            + (string (rand.Next(N)))
            + " #topic"
            + (string (rand.Next(N)))
            + " , , , "

        echoServer
        <? ClientRequest(
            "send",
            "",
            "user" + (string i),
            "password" + (string i),
            "",
            "tweet+user"
            + (string i)
            + "_"
            + (string j)
            + "th @user"
            + (string (rand.Next(N)))
            + " #topic"
            + (string (rand.Next(N))),
            "",
            "",
            ""
        )
        |> ignore


stopWatch.Stop()
let sendTime = stopWatch.Elapsed.TotalMilliseconds





let mutable step = 1
stopWatch = System.Diagnostics.Stopwatch.StartNew()
printfn "Zipf Subscribe ----------------------------------"

for i in 0 .. N - 1 do
    for j in 0 .. step .. N - 1 do
        if not (j = i) then
            let cmd =
                "subscribe, ,user"
                + (string j)
                + ",password"
                + (string j)
                + ",user"
                + (string i)
                + ", , , , "

            echoServer
            <? ClientRequest(
                "subscribe",
                "",
                "user" + (string j),
                "password" + (string j),
                "user" + (string i),
                "",
                "",
                "",
                ""
            )
            |> ignore

        step <- step + 1

stopWatch.Stop()
let zipfSubscribeTime = stopWatch.Elapsed.TotalMilliseconds


stopWatch = System.Diagnostics.Stopwatch.StartNew()

for i in 0 .. N - 1 do
    let cmd =
        "querying, ,user"
        + (string i)
        + ",password"
        + (string i)
        + ", , , , , "

    echoServer
    <? ClientRequest("querying", "", "user" + (string i), "password" + (string i), "", "", "", "", "")
    |> ignore


stopWatch.Stop()
let queryTime = stopWatch.Elapsed.TotalMilliseconds



stopWatch = System.Diagnostics.Stopwatch.StartNew()

for i in 0 .. N - 1 do
    let cmd =
        "#, , , , , ,#topic"
        + (string (rand.Next(N)))
        + ", ,"

    echoServer
    <? ClientRequest("#", "", "", "", "", "", "#topic" + (string (rand.Next(N))), "", "")
    |> ignore


stopWatch.Stop()
let hashtagTime = stopWatch.Elapsed.TotalMilliseconds


stopWatch = System.Diagnostics.Stopwatch.StartNew()

for i in 0 .. N - 1 do
    let cmd =
        "@, , , , , , ,@user"
        + (string (rand.Next(N)))
        + ","

    echoServer
    <? ClientRequest("@", "", "", "", "", "", "", "@user" + (string (rand.Next(N))), "")
    |> ignore


stopWatch.Stop()
let mentionTime = stopWatch.Elapsed.TotalMilliseconds


printfn "Trying random operations"
stopWatch = System.Diagnostics.Stopwatch.StartNew()
i <- 0
ii <- 0

while i < M do
    clientSimulation <! string (rand.Next(N))
    |> ignore

    i <- i + 1

while ii < M - 1 do
    Thread.Sleep(50)

stopWatch.Stop()
let randomTime = stopWatch.Elapsed.TotalMilliseconds


printfn "The time of register %d users is %f" N registerTime
printfn "The time of send 10 tweets is %f" sendTime
printfn "The time of Zipf subscribe %d users is %f" N zipfSubscribeTime
printfn "The time of query %d users is %f" N queryTime
printfn "The time of query %d hasgtag is %f" N hashtagTime
printfn "The time of query %d mention is %f" N mentionTime
printfn "The time of %d random operations is %f" M randomTime

printfn
    "Total Result: %f %f %f %f %f %f %f"
    registerTime
    sendTime
    zipfSubscribeTime
    queryTime
    hashtagTime
    mentionTime
    randomTime


system.Terminate() |> ignore
0 // return an integer exit code
