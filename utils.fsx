open System

type UserData = {
    userTweets: Set<string>
    following: Set<string>
    followers: Set<string>
}

type Tweets = {
    userid: string
    text: string
    time_of_tweet: DateTime
    retweet: bool
}

type Mentions = {
    mentionedTweets: Set<string>
}

type Hashtags = {
    hashtaggedTweets: Set<string>
}

type UserMessage = {
    message: string
    content: string
}

type Register = {
    id: string
}

type Follow = {
    myId: string
    toFollow: string
}

type Retweet = {
    id: string
    tid: string
}

type Search = {
    queryType: string
    query: string
}

type Connect = {
    id: string
}

type Disconnect = {
    id: string
}

type Remove = {
    id: string
}

let mentionParser (message: string) = 
    let mutable mentions: Set<string> = Set.empty
    let words = message.Trim().Split ' '
    for word in words do
        if word.[0] = '@' then
            mentions <- mentions.Add(word.ToLower())
    mentions

let hashtagParser (message: string) = 
    let mutable hashtags: Set<string> = Set.empty
    let words = message.Trim().Split ' '
    for word in words do
        if word.[0] = '#' then
            hashtags <- hashtags.Add(word.ToLower())
    hashtags