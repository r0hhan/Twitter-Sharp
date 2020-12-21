import websocket
try:
    import thread
except ImportError:
    import _thread as thread
import time

import json
from datetime import datetime
from pprint import pprint

# Function that is executed when the socket received a message
def on_message(ws, message):
    # Parsing the received JSON
    msg = json.loads(message) 
    if msg[0:3] == 'set':
        print("Server Said: \n", msg[6:-2])
    else:
        print("Server Said: ", msg)
    if msg == "Username already taken, connect again!\n":
        ws.close()

# Function that is executed when the socket errors out
def on_error(ws, error):
    print(error)

# Function that is executed when the socket closes
def on_close(ws):
    print("### closed connection ###")

# Utility function to send content on the socket
def sender(ws, content):
    str = json.dumps(content)
    ws.send(str)


# ======================================= Utility Functions =======================================
# The following utility functions are used to send messages to the Server
# Each function serializes the data into the JSON expected by the Server
# And then uses the sender() utility to send the message on the socket

# Function that registers a client
def register(ws, id):
    content = {
        "id": id
    }
    message = {
        'message': 'Register',
        'content': json.dumps(content)
    }
    sender(ws, message)

# Function that sends a connect message to the Server
# This is the only option if the user is currently disconnected
# In our system this is analogous to a Login
def connect(ws, id):
    content = {
        "id": id
    }
    message = {
        'message': "Connect",
        'content': json.dumps(content)
    }
    sender(ws, message)

# Function that sends a connect message to the Server
# This can only be used if the user is currently connected
# In our system this is analogous to a Logout
def disconnect(ws, id):
    content = {
        "id": id
    }
    message = {
        'message': 'Disconnect',
        'content': json.dumps(content)
    }
    sender(ws, message)

# Function that sends a follow message for the client
# Requires the client to specify the userid of the person they want to follow
def follow(ws, id):
    to_follow = input("Which user do you want to follow: ")
    content = {
        "myID": id,
        "toFollow": to_follow
    }
    message = {
        'message': 'Follow',
        'content': json.dumps(content)
    }
    sender(ws, message)

# Function that sends a tweet message
def tweet(ws, id):
    text = input("Enter tweet: ")
    content = {
        "userid": id,
        "text": text,
        "time_of_tweet": datetime.now(),
        "retweet": False
    }
    message = {
        'message': 'Tweets',
        'content': json.dumps(content, default=str)
    }
    sender(ws, message)

# Function that sends a retweet message
# Retweeet requires the client to specify the ID of the original tweet
def retweet(ws, id):
    tid = input("Enter tweet id you want to retweet: ")
    content = {
        "id": id,
        "tid": tid
    }
    message = {
        'message': 'Retweet',
        'content': json.dumps(content)
    }
    sender(ws, message)

# Function that handles the 3 different types of queries
# Each query except for "Following" requires an input
def search(ws, id):    
    query = int(input("What search do you want to execute:\n1. Hashtag\n2. Mention\n3. Subscribed Tweets\n"))
    switcher = {
        1: "Hashtag",
        2: "Mention",
        3: "Following"
    }

    content = {
        "queryType": switcher[query],
        "query": input("Enter search term: ") if query != 3 else id
    }
    message = {
        'message': 'Search',
        'content': json.dumps(content)
    }
    sender(ws, message)

def remove(ws, id):
    content = {
        "id": id
    }
    message = {
        'message': 'Remove',
        'content': json.dumps(content)
    }
    sender(ws, message)

# =================================================================================================

# Function that is executed when the socket connection is connected
def on_open(ws):
    def run(*args):
        # A pseudo switch case that is used to easily call the proper function
        switcher = { 
            1: disconnect,
            2: follow,
            3: tweet,
            4: retweet,
            5: search
        }
        uid = input("Enter Username for registration: ")
        register(ws, uid)
        flag = True
        while(True):
            if flag:
                # Menu driven interface for the client
                inp = int(input("What functionality do you want to execute:\n1. Disconnect\n2. Follow\n3. Tweet\n4. Retweet\n5. Search\n"))
                if inp == 1:
                    flag = False
                switcher[inp](ws, uid)
                print("request complete!")
            if not flag:
                c = input("Press Y to connect again or E to exit: ")
                if c.lower() == 'y':
                    connect(ws, uid)
                    flag = True
                elif c.lower() == 'e':
                    remove(ws, uid)
                    ws.close()
                    
    thread.start_new_thread(run, ())

if __name__ == "__main__":
    websocket.enableTrace(False)
    ws = websocket.WebSocketApp("ws://localhost:8080/websocket",
                              on_message = on_message,
                              on_error = on_error,
                              on_close = on_close)
    ws.on_open = on_open
    ws.run_forever()