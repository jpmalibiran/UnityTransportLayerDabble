using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;

public class NetworkServer : MonoBehaviour{
    public NetworkDriver m_Driver; //Listens; where the network begins and ends
    public ushort serverPort; // server port
    private NativeList<NetworkConnection> m_Connections; //List of connections. NativeList is an unmanaged memory list data structure (no garbage collection)

    void Start (){
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4; //Get this server side IP
        endpoint.Port = serverPort; // Apply server port

        //Try to bind driver to address
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen(); //Start listening for messages on successful bind

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent); //Set initial capacity to 16, set memory allocation to persistent (?)
    }

    //Sends a network message to client
    void SendToClient(string message, NetworkConnection c){
        //Start the process of sending messages. We pass in the pipeline that we want to send the message in. In this case it is the default channel (unreliable).
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c); 

        //Convert our message into bytes
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);

        //Use DataStreamWriter to write those bytes
        writer.WriteBytes(bytes);

        //Send the message and ends the process of sending messages
        m_Driver.EndSend(writer); 
    }

    //Cleans up driver and the unmanaged connection list. Unity or C# doesn't clean it; gotta be disposed manually 
    public void OnDestroy(){
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    //Adds network connection to our list of connections
    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        Debug.Log("Accepted a connection");

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i){
        //Convert our stream into bytes
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp); 
        stream.ReadBytes(bytes);

        //Convert byte into string data
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());

        //Convert data string into json object, and convert it into c# class
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        //convert json message into appropriate c# objects
        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg); 
                Debug.Log("Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE: //TODO: remove this. This is not needed because the server isn't going to send a message to itself
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.LogError("[Error] Server update message received! The server shouldn't receive Commands.SERVER_UPDATE.");
                break;
            default:
                Debug.Log("SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    //Turns the connection at the index into a default connections. Which will be cleaned up in Update()
    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
        m_Connections[i] = default(NetworkConnection);
    }

    void Update (){
        m_Driver.ScheduleUpdate().Complete(); //Tells the driver we're ready to listen for the next event

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++){
            if (!m_Connections[i].IsCreated){ //If there are any default connections: remove it
                //Note: NativeList.RemoveAtSwapBack() removes an item at a specified index.
                //This contrasts from NativeList.RemoveAt() in that NativeList.RemoveAtSwapBack() doesn't make an effort to retain the order of elements. Ergo, it's faster.
                m_Connections.RemoveAtSwapBack(i); //This
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection)){ //As long as it's not a default network connection            
            OnConnect(c);

            // Check if there is another new connection. If none, c will return a default connection and end the loop
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++){ //Loop through all active connections
            Assert.IsTrue(m_Connections[i].IsCreated); //Insure that the connection is valid. (ie. not a default connection)
            
            //Check type of connection and process it accordingly
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty){
                if (cmd == NetworkEvent.Type.Data){
                    // Fill the DataStreamReader stream with the data and send it to OnData(). i is index of teh connection taking place
                    OnData(stream, i); 
                }
                else if (cmd == NetworkEvent.Type.Disconnect){
                    OnDisconnect(i); // Disconnect
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}