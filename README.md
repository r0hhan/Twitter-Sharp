Rohit Choudhari   &emsp;  Rohan Hemant Wanare  
UFID: 5699-6044   &emsp;  UFID: 4511-0561

> Disclaimer: The dotnet SDK was recently updated to make `5.0` an official release. We have been using that to test this project.  

> Video Demo: [YouTube](https://www.youtube.com/watch?v=IQGhKvBD7AA)  
> Inputs: `None`  
> Requirements:   
    `F#: Suave StackExchange.Redis Newtonsoft.Json` <br> `Python 3.7+: websocket-client` <br>
> Redis needs to be run locally, we use the default `redis-server` configuration on `Ubuntu`.  
> Run:  
    
    1. Start Redis Server => `sudo service redis-server restart`
    2. Start Server => `dotnet fsi server.fsx`
    3. Start Clients => `python client.py`


## Design

- The design is pretty straightforward. We opted for a Client-Server model that uses a central server to process all messages and are using Redis running on a local store as a Memory store as a database. The server starts a websocket server at the default port `8080` and the client code is configured to connect to this port.
- The flow of execution is as follows:
    1. The server connects to Redis running at the default port.
    2. The server initially clears the Redis to start a new simulation. 
    3. Start up a client from the python terminal
    4. Each client is made to register with the server. This also "connects" the user.
    5. The clients have menu-driven interface, enter the number for the corresponding action to execute the action.
    6. The clients are online until the server is stopped or the client exits.
    7. The server can be stopped with `ctrl+c`
- Redis is a key-value store, so all data is Serialized using a JSON serializer and stored in the JSON format.
- All the data that is put on the socket is JSON serialized and is deserialized on both ends when received.

_Note_: A client that exits/closes a terminal is considered to have left the system and cannot reconnect.  
_Note_: The Python client has no processing and is only sending serialized messages to the server and deserializing the received messages for display.


## Functions

1. **Register**: A new user is created and added to the database. At the same time, the user is added to a set that maintains all the currently connected clients.
2. **Connect**: If a user chooses to disconnect from the server, they can reconnect to the server using this function. This is equivalent to logging in the twitter environment, so the client can carry all kinds of functionality.
3. **Disconnect**: A user can choose to disconnect if they are currently connected to the server. Being disconnected will result in removal from the connected clients set on the server and they will no longer receive tweets from the people they follow or be allowed to make requests. This is equivalent to logging out of the twitter environment.
4. **Tweet**: A user can send a new tweet using this function. The tweet is parsed and the appropriate mappings are added to the database. Once the tweet is registered, it is pushed by the server to all users that are live and have followed the user who initiated the tweet.
5. **Retweet**: A user can use this function to send a retweet provided they know the tweet id of a tweet by someone else. The server first fetched the original tweet's details and modifies it appropriately before registering it as a new tweet (with the retweet flag) and then processes it in the same way as a regular tweet.
6. **Search**: We have 3 search queries, each of them returns a set of tweets that satisfy the appropriate conditions.
7. **Remove**: Whenever the user exits the environment, our program closes the client's websocket as well as removes the client's details from the Database since after that another client with same username can register. This is equivalent to deleting your account.


## Database Structure

- Redis is a Key-value store
1. Users = Key: `'u'+username`
2. Tweets = Key: `tweetid` -> Incremental integer starting at 1
3. Hashtags = Key: `#<value>`
4. Mentions = Key: `@<value>`
> The code has been annotated with comments that may help understand more about the specific implementation.

## Errors Handled

1. Users cannot follow themselves
2. Two users can't have the same username
3. Users cannot follow users that don't exist
4. Users cannot retweet a tweet that doesn't exist
> Client receives a relevant error message whenever an error occurs