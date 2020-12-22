#r "nuget: Newtonsoft.Json"
#r "nuget: Suave"
#r "nuget: StackExchange.Redis"

#load "utils.fsx"

open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils

open System
open System.Net
open System.Collections.Generic
open Utils

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

open StackExchange.Redis
open Newtonsoft.Json

// Creates the connection to the Redis server
let cx = ConnectionMultiplexer.Connect @"redis-11926.c56.east-us.azure.cloud.redislabs.com:11926,allowAdmin=true,password=GPNUK2mhf0KjQ5CANzyD2GvzgDIPeHGw"
let redisServer = cx.GetServer(cx.GetEndPoints().[0])
printfn "\n\nFlushing Redis ... "
// Flushing the data initially for a new start
redisServer.FlushDatabase()

module Database = 
    let cache = cx.GetDatabase()

    // Utility functions

    // Usage: 
    // let user = {followers = [| "u2"; "u3" |]}
    // let value = redisSend user "123"
    let redisSend (key: string) (obj: 'a) =
        let serialUser = JsonConvert.SerializeObject obj
        let value = cache.StringSet(RedisKey key, RedisValue serialUser)
        value

    // Usage: 
    // let rvalue = redisReceive "test"
    let redisReceive (key: string) = 
        let value = cache.StringGet(RedisKey key)
        string value

    let redisRemove (key: string) =
        let value = cache.KeyDelete(RedisKey key)
        string value


let mutable tweetID = 1
let connectedClients = new Dictionary<string, WebSocket>()

let ws (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true
    
        while loop do
            let! msg = webSocket.read()
            match msg with

                | (Text, data, true) ->

                    let str = UTF8.toString data
                    let incoming = JsonConvert.DeserializeObject<UserMessage> str 
                
                    match incoming.message with

                        |"Register" ->
                            let register = JsonConvert.DeserializeObject<Register> incoming.content
                            let userid = "u" + register.id
                            let mutable temp = null
                            let value = Database.redisReceive (userid)
                            if not (value = null) then
                                temp <- sprintf "Username already taken, connect again!\n"
                            else
                                let user: UserData = {userTweets = Set.empty; following = Set.empty; followers = Set.empty}
                                let uvalue = Database.redisSend userid user
                                if uvalue then
                                    temp <- sprintf "Connected Successfully!\n"
                                    connectedClients.Add(register.id, webSocket)
                                    printfn "Connected: %A" register.id       
                                else
                                    temp <- sprintf "Registration failed, please try again!\n"
                                    printfn "Registration FAILURE for user %A" userid

                            let response = JsonConvert.SerializeObject temp
                            let byteResponse = 
                                response
                                |> System.Text.Encoding.ASCII.GetBytes
                                |> ByteSegment
                            do! webSocket.send Text byteResponse true

                        | "Connect" ->
                            let connect = JsonConvert.DeserializeObject<Connect> incoming.content
                            connectedClients.Add(connect.id, webSocket)
                            let temp = sprintf "Connected Successfully!\n"
                            let response = JsonConvert.SerializeObject temp
                            let byteResponse = 
                                response
                                |> System.Text.Encoding.ASCII.GetBytes
                                |> ByteSegment
                            do! webSocket.send Text byteResponse true
                            printfn "Connected: %A" connect.id

                        | "Disconnect" ->
                            let disconnect = JsonConvert.DeserializeObject<Disconnect> incoming.content
                            connectedClients.Remove(disconnect.id) |> ignore
                            let temp = sprintf "Disconnected Successfully!\n"
                            let response = JsonConvert.SerializeObject temp
                            let byteResponse = 
                                response
                                |> System.Text.Encoding.ASCII.GetBytes
                                |> ByteSegment
                            do! webSocket.send Text byteResponse true
                            printfn "Disconnected: %A" disconnect.id

                        | "Follow" ->
                            // First we add a binding from the user who makes the follow request and add the requested followee to the user data
                            let follow = JsonConvert.DeserializeObject<Follow> incoming.content
                            let v1 = Database.redisReceive ("u" + follow.myId)
                            let v2 = Database.redisReceive ("u" + follow.toFollow)
                            let mutable temp = null
                            if follow.myId = follow.toFollow then
                                temp <- sprintf "User cannot follow self\n"
                            elif v2 = null then
                                temp <- sprintf "User does not exist\n"
                            else
                                let jvalue = JsonConvert.DeserializeObject<UserData> v1
                                let followingSet = jvalue.following.Add(follow.toFollow)
                                let userdata: UserData = {userTweets = jvalue.userTweets; following = followingSet; followers = jvalue.followers}
                                Database.redisSend ("u" + follow.myId) userdata |> ignore

                                // Then we make the reverse binding and add the follower to the followee's following set
                                let jvalue = JsonConvert.DeserializeObject<UserData> v2
                                let followerSet = jvalue.followers.Add(follow.myId)
                                let userdata: UserData = {userTweets = jvalue.userTweets; following = jvalue.following; followers = followerSet}
                                Database.redisSend ("u" + follow.toFollow) userdata |> ignore

                                temp <- sprintf "Follow Successful!\n"
                            let response = JsonConvert.SerializeObject temp
                            let byteResponse = 
                                response
                                |> System.Text.Encoding.ASCII.GetBytes
                                |> ByteSegment
                            do! webSocket.send Text byteResponse true

                        | "Tweets" ->
                            // This adds the new tweet under a new Key to the database with the details
                            // Key: TweetID || Value: Tweet Details
                            let tweet = JsonConvert.DeserializeObject<Tweets> incoming.content
                            Database.redisSend (string tweetID) tweet |> ignore
                            
                            // This block starts with fetching the set of tweets the user has already made and adds the new tweet to it
                            // Structure of tweetMsg: Tweets = {userid = userid; text = text; time_of_tweet = time_of_tweet; retweet = retweet}
                            let value = Database.redisReceive ("u" + tweet.userid)
                            let jvalue = JsonConvert.DeserializeObject<UserData> value
                            let listOfTweets = jvalue.userTweets.Add(string tweetID)
                            let userdata: UserData = {userTweets = listOfTweets; following = jvalue.following; followers = jvalue.followers}
                            Database.redisSend ("u" + tweet.userid) userdata |> ignore

                            // This block of code parses the tweet text to fetch all the Hashtags in the text and to add it to the database
                            // Key: the hashtag || Value: Set of Tweets that contain the hashtag
                            let hashtagSet = hashtagParser tweet.text
                            let mutable value: Hashtags = {hashtaggedTweets = Set.empty}
                            for hashtag in hashtagSet do
                                let kvalue = Database.redisReceive hashtag
                                if not (kvalue = null) then
                                    value <- JsonConvert.DeserializeObject<Hashtags> kvalue
                                let jvalue = value.hashtaggedTweets.Add(string tweetID)
                                let hvalue: Hashtags = {hashtaggedTweets = jvalue}
                                Database.redisSend hashtag hvalue |> ignore

                            // This block of code parses the tweet text to fetch all the mentions in the text and to add it to the database
                            // Key: the mention || Value: Set of Tweets that contain the mention
                            let mentionsSet = mentionParser tweet.text
                            let mutable value: Mentions = {mentionedTweets = Set.empty}
                            for mention in mentionsSet do
                                let kvalue = Database.redisReceive mention
                                if not (kvalue = null) then
                                    value <- JsonConvert.DeserializeObject<Mentions> kvalue
                                let jvalue = value.mentionedTweets.Add(string tweetID)
                                let mvalue: Mentions = {mentionedTweets = jvalue}
                                Database.redisSend mention mvalue |> ignore

                            // This block of code iterates through all the followers the user that initiated the Tweet has
                            // and pushes the tweet to each of their "live feeds"
                            let temp = sprintf "TweetID: %A\nTweet: %A" tweetID tweet
                            let response = JsonConvert.SerializeObject temp
                            for follower in userdata.followers do
                                if connectedClients.ContainsKey(follower) then
                                    let sock = connectedClients.Item(follower)
                                    let byteResponse = 
                                        response
                                        |> System.Text.Encoding.ASCII.GetBytes
                                        |> ByteSegment
                                    do! sock.send Text byteResponse true

                            // increment tweetid
                            tweetID <- tweetID + 1

                        | "Retweet" ->
                            // Retweets are handled in the exact same way as a regular Tweet but have the "Retweet" flag set
                            // This block fetches the details of the tweet to be retweeted
                            let retweet = JsonConvert.DeserializeObject<Retweet> incoming.content
                            let mutable temp = null
                            let value = Database.redisReceive retweet.tid
                            if value = null then
                                temp <- sprintf "Retweet failed! Tweet with tweet Id %A does not exist." retweet.tid
                                let response = JsonConvert.SerializeObject temp
                                let byteResponse = 
                                    response
                                    |> System.Text.Encoding.ASCII.GetBytes
                                    |> ByteSegment
                                do! webSocket.send Text byteResponse true
                            else
                                let jvalue = JsonConvert.DeserializeObject<Tweets> value
                                let tweet: Tweets = {userid = retweet.id; text = jvalue.text; time_of_tweet = DateTime.Now; retweet = true}
                                Database.redisSend (string tweetID) tweet |> ignore

                                // This block starts with fetching the set of tweets the user has already made and adds the new tweet to it
                                let value = Database.redisReceive ("u" + retweet.id)
                                let jvalue = JsonConvert.DeserializeObject<UserData> value
                                let listOfTweets = jvalue.userTweets.Add(string tweetID)
                                let userdata: UserData = {userTweets = listOfTweets; following = jvalue.following; followers = jvalue.followers}
                                Database.redisSend ("u" + retweet.id) userdata |> ignore

                                // This block of code parses the tweet text to fetch all the Hashtags in the text and to add it to the database
                                // Key: the hashtag || Value: Set of Tweets that contain the hashtag
                                let hashtagSet = hashtagParser tweet.text
                                let mutable value: Hashtags = {hashtaggedTweets = Set.empty}
                                for hashtag in hashtagSet do
                                    let kvalue = Database.redisReceive hashtag
                                    if not (kvalue = null) then
                                        value <- JsonConvert.DeserializeObject<Hashtags> kvalue
                                    let jvalue = value.hashtaggedTweets.Add(string tweetID)
                                    let hvalue: Hashtags = {hashtaggedTweets = jvalue}
                                    Database.redisSend hashtag hvalue |> ignore

                                // This block of code parses the tweet text to fetch all the mentions in the text and to add it to the database
                                // Key: the mention || Value: Set of Tweets that contain the mention
                                let mentionsSet = mentionParser tweet.text
                                let mutable value: Mentions = {mentionedTweets = Set.empty}
                                for mention in mentionsSet do
                                    let kvalue = Database.redisReceive mention
                                    if not (kvalue = null) then
                                        value <- JsonConvert.DeserializeObject<Mentions> kvalue
                                    let jvalue = value.mentionedTweets.Add(string tweetID)
                                    let mvalue: Mentions = {mentionedTweets = jvalue}
                                    Database.redisSend mention mvalue |> ignore
                                
                                // This block of code iterates through all the followers the user that initiated the Tweet has
                                // and pushes the tweet to each of their "live feeds"
                                let temp = sprintf "TweetID: %A\nTweet: %A" tweetID tweet
                                let response = JsonConvert.SerializeObject temp
                                for follower in userdata.followers do
                                    if connectedClients.ContainsKey(follower) then
                                        let sock = connectedClients.Item(follower)                            
                                        let byteResponse = 
                                            response
                                            |> System.Text.Encoding.ASCII.GetBytes
                                            |> ByteSegment
                                        do! sock.send Text byteResponse true

                                // increment tweetID
                                tweetID <- tweetID + 1

                        | "Search" ->
                            // Query is a Union type and is matched with the respective type of query
                            let search = JsonConvert.DeserializeObject<Search> incoming.content
                            printfn "query received %A" search.queryType
                            let mutable temp = null
                            match search.queryType with
                                | "Following" ->
                                    // We first fetch the user's details
                                    let userid = search.query
                                    let mutable tweetSet = Set.empty
                                    let value = Database.redisReceive ("u" + userid)
                                    let user = JsonConvert.DeserializeObject<UserData> value
                                    
                                    // Then we iterate through each followed user and collect their respsective tweets
                                    for followedUser in user.following do
                                        let hvalue = Database.redisReceive ("u" + followedUser)
                                        let userdata = JsonConvert.DeserializeObject<UserData> hvalue
                                        for tid in userdata.userTweets do
                                            let jvalue = Database.redisReceive tid
                                            let tweet = JsonConvert.DeserializeObject<Tweets> jvalue
                                            let temp = sprintf "TweetID: %A\nTweet: %A" tid tweet
                                            tweetSet <- tweetSet.Add(temp)
                                    
                                    if tweetSet.IsEmpty then
                                        temp <- sprintf "No tweets match your query!"
                                    else
                                        temp <- sprintf "%A" tweetSet
                                        
                                    let response = JsonConvert.SerializeObject temp
                                    let byteResponse = 
                                        response
                                        |> System.Text.Encoding.ASCII.GetBytes
                                        |> ByteSegment
                                    do! webSocket.send Text byteResponse true

                                    printfn "Finished %A query." search.queryType

                                    
                                | "Hashtag" ->
                                    // We fetch the IDs of tweets the hashtag was a part of
                                    // Then we iterate through them to get the actual tweet details
                                    // And collect them in a set
                                    let tag = search.query
                                    let mutable tweetSet = Set.empty
                                    let value = Database.redisReceive tag
                                    if not (value = null) then
                                        let taggedTweets = JsonConvert.DeserializeObject<Hashtags> value
                                        
                                        for tid in taggedTweets.hashtaggedTweets do
                                            let jvalue = Database.redisReceive tid
                                            let tweet = JsonConvert.DeserializeObject<Tweets> jvalue
                                            let temp = sprintf "TweetID: %A\nTweet: %A" tid tweet
                                            tweetSet <- tweetSet.Add(temp)

                                    if tweetSet.IsEmpty then
                                        temp <- sprintf "No tweets match your query!"
                                    else
                                        temp <- sprintf "%A" tweetSet
                                        
                                    let response = JsonConvert.SerializeObject temp
                                    let byteResponse = 
                                        response
                                        |> System.Text.Encoding.ASCII.GetBytes
                                        |> ByteSegment
                                    do! webSocket.send Text byteResponse true
                                    
                                | "Mention" ->
                                    // We fetch the IDs of tweets the mention was a part of
                                    // Then we iterate through them to get the actual tweet details
                                    // And collect them in a set 
                                    let someID = search.query
                                    let mutable tweetSet = Set.empty
                                    let value = Database.redisReceive someID
                                    if not (value = null) then
                                        let mentionedIn = JsonConvert.DeserializeObject<Mentions> value 
                                    
                                        for tid in mentionedIn.mentionedTweets do
                                            let jvalue = Database.redisReceive tid
                                            let tweet = JsonConvert.DeserializeObject<Tweets> jvalue
                                            let temp = sprintf "TweetID: %A\nTweet: %A" tid tweet
                                            tweetSet <- tweetSet.Add(temp)

                                    if tweetSet.IsEmpty then
                                        temp <- sprintf "No tweets match your query!"
                                    else
                                        temp <- sprintf "%A" tweetSet
                                    let response = JsonConvert.SerializeObject temp
                                    let byteResponse = 
                                        response
                                        |> System.Text.Encoding.ASCII.GetBytes
                                        |> ByteSegment
                                    do! webSocket.send Text byteResponse true
                                
                                | _ ->
                                    temp <- sprintf "Invalid Function"
                                    let response = JsonConvert.SerializeObject temp
                                    let byteResponse =
                                        response
                                        |> System.Text.Encoding.ASCII.GetBytes
                                        |> ByteSegment
                                    do! webSocket.send Text byteResponse true
                        
                        | "Remove" ->
                            // This message is used to delete a user's profile from the database 
                            let remove = JsonConvert.DeserializeObject<Remove> incoming.content
                            let value = Database.redisRemove ("u" + remove.id)
                            printfn "key delete response = %A" value

                        | _ ->
                            let temp = sprintf "Invalid Function"
                            let response = JsonConvert.SerializeObject temp
                            let byteResponse =
                                response
                                |> System.Text.Encoding.ASCII.GetBytes
                                |> ByteSegment
                            do! webSocket.send Text byteResponse true

                | (Close, _, _) ->
                    let emptyResponse = [| |] |> ByteSegment
                    do! webSocket.send Close emptyResponse true

                    // after sending a Close message, stop the loop
                    loop <- false

                | _ -> ()
    }

let app : WebPart = 
    choose [
        // This assigns the handler for the websocket
        path "/websocket" >=> handShake ws
        NOT_FOUND "Found no handlers." ]

// starts the server
// startWebServer { defaultConfig with logger = Targets.create Verbose [| |] } app

let cfg =
  { defaultConfig with
      bindings =
        [ HttpBinding.create HTTP IPAddress.Loopback 80us
          HttpBinding.createSimple HTTP "0.0.0.0" 9000 ]
      listenTimeout = TimeSpan.FromMilliseconds 3000. }
app
|> startWebServer cfg