#r "nuget: Akka"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.TestKit"
#load "Message.fsx"

open System
open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Message

let configuration =
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
                    port = 8777
                    hostname = localhost
                }
            }
        }"
    )

let system =
    ActorSystem.Create("Server", configuration)



type Tweet(tweetId: string, text: string, isReTweet: bool) =
    member this._TweetId = tweetId
    member this._Text = text
    member this._IsReTweet = isReTweet

    override this.ToString() =
        let mutable res = ""

        if isReTweet then
            res <- sprintf "~[retweet][%s]%s" this._TweetId this._Text
        else
            res <- sprintf "[%s]%s" this._TweetId this._Text

        res



type User(userName: string, password: string) =
    let mutable subscribes: User list = List.empty
    let mutable tweets: Tweet list = List.empty
    member this._UserName = userName
    member this._Password = password

    member this.AddSubscriber x =
        subscribes <- List.append subscribes [ x ]

    member this.GetSubscribers() = subscribes
    member this.AddTweet x = tweets <- List.append tweets [ x ]
    member this.GetTweets() = tweets
    override this.ToString() = this._UserName


type Twitter() =
    let mutable tweets = new Map<string, Tweet>([])
    let mutable users = new Map<string, User>([])
    let mutable hashtags = new Map<string, Tweet list>([])
    let mutable mentions = new Map<string, Tweet list>([])


    member this.AddTweet(tweet: Tweet) =
        tweets <- tweets.Add(tweet._TweetId, tweet)

    member this.AddUser(user: User) =
        users <- users.Add(user._UserName, user)

    member this.AddToHashTag hashtag tweet =

        let mutable map = hashtags

        if not <| map.ContainsKey(hashtag) then
            map <- map.Add(hashtag, [])

        map <- map.Add(hashtag, map.[hashtag] @ [ tweet ])
        hashtags <- map

    member this.AddToMention mention tweet =

        let mutable map = mentions

        if not <| map.ContainsKey(mention) then
            map <- map.Add(mention, [])

        map <- map.Add(mention, map.[mention] @ [ tweet ])
        mentions <- map

    member this.Register username password =
        if users.ContainsKey(username) then
            "-[error] Username taken already."
        else
            let user = new User(username, password)
            this.AddUser user
            user.AddSubscriber user

            "+[success] Register success username: "
            + username
            + "  password: "
            + password

    member this.SendTweet username password text isReTweet =
        if not (this.Authentication username password) then
            "-[error] Wrong password or username"
        else
            let tweet =
                new Tweet(System.DateTime.Now.ToFileTimeUtc() |> string, text, isReTweet)

            let user = users.[username]
            user.AddTweet tweet
            this.AddTweet tweet
            let idx1 = text.IndexOf("#")

            if idx1 <> -1 then
                let idx2 = text.IndexOf(" ", idx1)
                let hashtag = text.[idx1..idx2 - 1]
                this.AddToHashTag hashtag tweet

            let idx1 = text.IndexOf("@")

            if idx1 <> -1 then
                let idx2 = text.IndexOf(" ", idx1)
                let mention = text.[idx1..idx2 - 1]
                this.AddToMention mention tweet

            "+[success] sent twitter: " + tweet.ToString()

    member this.Authentication username password =

        if not (users.ContainsKey(username)) then
            printfn "-[error] User does not exist."
            false
        else
            let user = users.[username]
            user._Password = password

    member this.GetUser username =
        if not (users.ContainsKey(username)) then
            printfn "-[error] User does not exist."
            new User("", "")
        else
            users.[username]

    member this.Subscribe username1 password username2 =

        if not (this.Authentication username1 password) then
            "-[error] Authentication fail."
        else
            let user1 = this.GetUser username1
            let user2 = this.GetUser username2
            user1.AddSubscriber user2

            "+[success] "
            + username1
            + " subscribed to "
            + username2

    member this.ReTweet username password text =
        "~[retweet]"
        + (this.SendTweet username password text true)

    member this.QueryTweetsSubscribed username password =

        if not (this.Authentication username password) then
            "-[error] Authentication failed."
        else
            let user = this.GetUser username

            let res1 =
                (List.collect (fun (x: User) -> x.GetTweets()) (user.GetSubscribers()))
                |> List.map (fun x -> x.ToString())
                |> String.concat "\n"

            "+[success] queryTweetsSubscribed\n" + res1

    member this.queryHashTag hashtag =
        if not (hashtags.ContainsKey(hashtag)) then
            "-[error] #Hashtag does not exist."
        else
            let res1 =
                hashtags.[hashtag]
                |> List.map (fun x -> x.ToString())
                |> String.concat "\n"

            "+[success] queryHashTag\n" + res1

    member this.QueryMention mention =
        let mutable res = ""

        if not (mentions.ContainsKey(mention)) then
            "-[error] @Mention does not exist."
        else
            let res1 =
                mentions.[mention]
                |> List.map (fun x -> x.ToString())
                |> String.concat "\n"

            "+[success] queryMention" + "\n" + res1

    override this.ToString() =
        "Twitter"
        + "\n"
        + tweets.ToString()
        + "\n"
        + users.ToString()
        + "\n"
        + hashtags.ToString()
        + "\n"
        + mentions.ToString()


let twitter = new Twitter()


type Message =
    | Register of string * string * string * string
    | Send of string * string * string * string * bool
    | Subscribe of string * string * string * string
    | Retweet of string * string * string * string
    | Hashtag of string * string
    | Mention of string * string
    | Tweets of string * string * string


let actorRegister (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Register (post, register, username, password) ->
                let res = twitter.Register username password
                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()

let actorSendMsg (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            let sender_path =
                mailbox.Sender().Path.ToStringWithAddress()

            match message with
            | Send (post, username, password, tweet_content, false) ->
                let res =
                    twitter.SendTweet username password tweet_content false

                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()

let actorSubscribe (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Subscribe (post, username, password, target_username) ->
                let res =
                    twitter.Subscribe username password target_username

                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()

let actorRetweet (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Retweet (post, username, password, tweet_content) ->
                let res =
                    twitter.ReTweet username password tweet_content

                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()


let actorQueryTweets (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Tweets (post, username, password) ->
                let res =
                    twitter.QueryTweetsSubscribed username password

                sender <? res |> ignore

            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()

let actorQueryHashtags (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Hashtag (post, queryhashtag) ->
                let res = twitter.queryHashTag queryhashtag
                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()

let actorMentions (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | Mention (post, at) ->
                let res = twitter.QueryMention at
                sender <? res |> ignore
            | _ -> failwith "unknown message"

            return! loop ()
        }

    loop ()


let registerUser = spawn system "service1" actorRegister

let sendMsg = spawn system "service2" actorSendMsg
let subscribe = spawn system "service3" actorSubscribe
let retweet = spawn system "service4" actorRetweet

let queryTweets = spawn system "service5" actorQueryTweets

let queryHashtags =
    spawn system "service6" actorQueryHashtags

let mention = spawn system "service7" actorMentions

// for the server, received string and dispatch
let actorRouter (mailbox: Actor<_>) =
    let rec loop () =
        actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()

            match message with
            | ClientRequest (opt, post, username, password, target_username, tweet_content, queryhashtag, at, register) ->

                //(opt,POST,username,password,target_username,tweet_content,queryhashtag,at,register)

                printfn "[message received] %A" message

                let mutable opt = opt
                let mutable POST = post
                let mutable username = username
                let mutable password = password
                let mutable targetUsername = target_username
                let mutable tweetContent = tweet_content
                let mutable queryhashtag = queryhashtag
                let mutable at = at
                let mutable register = register
                let mutable task = registerUser <? Register("", "", "", "")

                if opt = "reg" then
                    printfn "[Register] username:%s password: %s" username password

                    task <-
                        registerUser
                        <? Register(POST, register, username, password)

                if opt = "send" then
                    printfn "[send] username:%s password: %s tweet_content: %s" username password tweet_content

                    task <-
                        sendMsg
                        <? Send(POST, username, password, tweet_content, false)

                if opt = "subscribe" then
                    printfn
                        "[subscribe] username:%s password: %s subscribes username: %s"
                        username
                        password
                        target_username

                    task <-
                        subscribe
                        <? Subscribe(POST, username, password, target_username)

                if opt = "retweet" then
                    printfn "[retweet] username:%s password: %s tweet_content: %s" username password tweet_content

                    task <-
                        retweet
                        <? Retweet(POST, username, password, tweet_content)

                if opt = "querying" then
                    printfn "[querying] username:%s password: %s" username password
                    task <- queryTweets <? Tweets(POST, username, password)

                if opt = "#" then
                    printfn "[#Hashtag] %s: " queryhashtag
                    task <- queryHashtags <? Hashtag(POST, queryhashtag)

                if opt = "@" then
                    printfn "[@mention] %s" at
                    task <- mention <? Mention(POST, at)

                let response = Async.RunSynchronously(task, 1000)
                sender <? response |> ignore
                printfn "[Result]: %s" response

            | _ -> printfn "Weird message"

            return! loop ()
        }

    loop ()

let router = spawn system "RouterServer" actorRouter


printfn "------------------------------------------------- \n "
printfn "-------------------------------------------------   "
printfn "Twitter Server is running...   "
printfn "-------------------------------------------------   "

// For function reg
Console.ReadLine() |> ignore

printfn "-----------------------------------------------------------\n"
0
