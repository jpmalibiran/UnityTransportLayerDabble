using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

public class NetworkServer : MonoBehaviour{
    public NetworkDriver m_Driver; //Listens; where the network begins and ends
    public ushort serverPort; // server port
    private NativeList<NetworkConnection> m_Connections; //List of connections. NativeList is an unmanaged memory list data structure (no garbage collection)

    private Dictionary<int, NetworkObjects.NetworkPlayer> m_clientIDDict; //the int key represents NetworkConnection.internalId
    private ushort clientIDCounter = 1;


    private void Start (){
        Debug.Log("[Notice] Setting up server...");
        print("[Notice] Setting up server...");

        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4; //Get this server side IP
        endpoint.Port = serverPort; // Apply server port

        //Try to bind driver to address
        if (m_Driver.Bind(endpoint) != 0)
            Debug.LogError("[Error] Failed to bind to port " + serverPort);
        else
            m_Driver.Listen(); //Start listening for messages on successful bind
            Debug.Log("[Notice] Server Ready.");

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent); //Set initial capacity to 16, set memory allocation to persistent (?)
    }

    private void Update (){
        m_Driver.ScheduleUpdate().Complete(); //Tells the driver we're ready to listen for the next event

        CleanUpConnections();
        AcceptNewConnections();
        ReadIncomingMessages();

        // CleanUpConnections
        //for (int i = 0; i < m_Connections.Length; i++){
        //    if (!m_Connections[i].IsCreated){ //If there are any default connections: remove it
        //        //Note: NativeList.RemoveAtSwapBack() removes an item at a specified index.
        //        //This contrasts from NativeList.RemoveAt() in that NativeList.RemoveAtSwapBack() doesn't make an effort to retain the order of elements. Ergo, it's faster.
        //        m_Connections.RemoveAtSwapBack(i); //This
        //        --i;
        //    }
        //}

        // AcceptNewConnections
        //NetworkConnection c = m_Driver.Accept();
        //while (c != default(NetworkConnection)) { //As long as it's not a default network connection            
        //    OnConnect(c);

        //    // Check if there is another new connection. If none, c will return a default connection and end the loop
        //    c = m_Driver.Accept();
        //}

        // Read Incoming Messages
        //DataStreamReader stream;
        //for (int i = 0; i < m_Connections.Length; i++){ //Loop through all active connections
        //    Assert.IsTrue(m_Connections[i].IsCreated); //Insure that the connection is valid. (ie. not a default connection)
            
        //    //Check type of connection and process it accordingly
        //    NetworkEvent.Type cmd;
        //    cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
        //    while (cmd != NetworkEvent.Type.Empty){
        //        if (cmd == NetworkEvent.Type.Data){
        //            // Fill the DataStreamReader stream with the data and send it to OnData(). i is index of teh connection taking place
        //            OnData(stream, i); 
        //        }
        //        else if (cmd == NetworkEvent.Type.Disconnect){
        //            OnDisconnect(i); // Disconnect
        //        }

        //        cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
        //    }
        //}
    }

    //Cleans up driver and the unmanaged connection list. Unity or C# doesn't clean it; gotta be disposed manually 
    public void OnDestroy(){
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    //Adds network connection to our list of connections
    private void OnConnect(NetworkConnection c){
        NetworkObjects.NetworkPlayer newClient;

        Debug.Log("[Notice] New client connected to server.");

        m_Connections.Add(c);
        newClient = new NetworkObjects.NetworkPlayer();

        newClient.clientID = GetNewClientID(); //Assign new ID

        m_clientIDDict.Add(c.InternalId, newClient);

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    private void CleanUpConnections() {
        for (int i = 0; i < m_Connections.Length; i++){
            if (!m_Connections[i].IsCreated){ //If there are any default connections: remove it
                //Note: NativeList.RemoveAtSwapBack() removes an item at a specified index.
                //This contrasts from NativeList.RemoveAt() in that NativeList.RemoveAtSwapBack() doesn't make an effort to retain the order of elements. Ergo, it's faster.
                m_Connections.RemoveAtSwapBack(i); //This
                --i;
            }
        }
    }

    private void AcceptNewConnections() {
        NetworkConnection c = m_Driver.Accept();
        
        while (c != default(NetworkConnection)) { //As long as it's not a default network connection            
            OnConnect(c);

            // Check if there is another new connection. If none, c will return a default connection and end the loop
            c = m_Driver.Accept();
        }
    }

    private void ReadIncomingMessages() {
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++){ //Loop through all active connections
            Assert.IsTrue(m_Connections[i].IsCreated); //Insure that the connection is valid. (ie. not a default connection)
            
            //Check type of connection and process it accordingly
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty){
                if (cmd == NetworkEvent.Type.Data){
                    // Fill the DataStreamReader stream with the data and send it to OnData(). i is index of the connection taking place
                    OnData(stream, i); 
                }
                else if (cmd == NetworkEvent.Type.Disconnect){
                    OnDisconnect(i); // Disconnect
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }

    //Sends a network message to client
    private void SendToClient(string message, NetworkConnection c){
        //Start the process of sending messages. We pass in the pipeline that we want to send the message in. In this case it is the default channel (unreliable).
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c); 

        //Convert our message into bytes
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);

        //Use DataStreamWriter to write those bytes
        writer.WriteBytes(bytes);

        //Send the message and ends the process of sending messages
        m_Driver.EndSend(writer); 
    }
    
    private void OnData(DataStreamReader stream, int i){
        //Convert our stream into bytes
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp); 
        stream.ReadBytes(bytes);

        //Convert byte into string data
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());

        //Convert data string into json object, and convert it into c# class
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        //convert json message into appropriate c# objects
        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg); 
                Debug.Log("[Notice] Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("[Notice] Player update message received!");
                break;
            case Commands.SERVER_UPDATE: //TODO: remove this. This is not needed because the server isn't going to send a message to itself
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.LogError("[Error] Server update message received! The server shouldn't receive Commands.SERVER_UPDATE.");
                break;
            case Commands.PING:
                Debug.Log("[Notice] Ping received from " + m_Connections[i].InternalId + "(InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + "(clientID)");
                PongClientResponse(i); // Send back Pong message
                break;
            case Commands.CLIENT_HANDSHAKE:
                Debug.Log("[Notice] Handshake from client received! " + m_Connections[i].InternalId + "(InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + "(clientID)");
                SendServerHandshake(i, m_clientIDDict[m_Connections[i].InternalId].clientID); //Send back handshake
                UpdateClientsWithNewPlayer(m_clientIDDict[m_Connections[i].InternalId].clientID); //Send all connected clients the new player data
                break;
            default:
                Debug.LogError("[Error] SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    //Turns the connection at the index into a default connections. Which will be cleaned up in Update()
    private void OnDisconnect(int i){
        Debug.Log("[Notice] Client disconnected from server. " + m_Connections[i].InternalId + "(InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + "(clientID)");

        //Remove entry in Dictionary m_clientIDDict
        if (m_clientIDDict.ContainsKey(m_Connections[i].InternalId)) {
            m_clientIDDict.Remove(m_Connections[i].InternalId);
        }

        //Remove entry in NativeList m_Connections
        m_Connections[i] = default(NetworkConnection);
    }

    private ushort GetNewClientID() {
        //Tick up counter so that the next connected client gets a new unique ID. 
        //Note: Obviously there will be issues if there are over 65530 concurrent players connecting. TODO check if ID is taken before assigning it.
        clientIDCounter++;

        if (clientIDCounter >= 65530) { //Reset counter near ushort max
            clientIDCounter = 1;
        }

        return clientIDCounter;
    }

    private void PongClientResponse(int getConnectionIndex) {
        NetworkHeader pongMsg = new NetworkHeader(); 
        pongMsg.cmd = Commands.PONG;
        SendToClient(JsonUtility.ToJson(pongMsg), m_Connections[getConnectionIndex]);    
        Debug.Log("[Notice] Pong sent to Client! " + m_Connections[getConnectionIndex].InternalId + "(InternalId) " + m_clientIDDict[m_Connections[getConnectionIndex].InternalId].clientID + "(clientID)");
    }

    private void SendServerHandshake(int targetClientIndex, ushort setClientID) {
        ServerHandshakeMsg msg;

        Debug.Log("[Notice] Sending player list and assigned ID to newly connected player... ");

        //Create handshake object and assign client its ID
        msg = new ServerHandshakeMsg(setClientID);

        //Setting up list of player data (if any) to send to newly connected player
        foreach(KeyValuePair<int, NetworkObjects.NetworkPlayer> client in m_clientIDDict){
            msg.players.Add(client.Value);
        }

        //Send handshake to client
        SendToClient(JsonUtility.ToJson(msg), m_Connections[targetClientIndex]);
        Debug.Log("[Notice] Handshake sent to Client! " + m_Connections[targetClientIndex].InternalId + "(InternalId) " + m_clientIDDict[m_Connections[targetClientIndex].InternalId].clientID + "(clientID)");

        ShowClientList();
    }

    private void UpdateClientsWithNewPlayer(ushort getClientID) {
        PlayerUpdateMsg newPlayerMsg;

        Debug.Log("[Notice] Sending new player data to all connected clients...");

        newPlayerMsg = new PlayerUpdateMsg(Commands.NEW_PLAYER);

        newPlayerMsg.player.clientID = getClientID;
        newPlayerMsg.player.cubePosition = m_clientIDDict[getClientID].cubePosition;
        newPlayerMsg.player.cubeOrientation = m_clientIDDict[getClientID].cubeOrientation;
        newPlayerMsg.player.cubeColor = Color.white;
        newPlayerMsg.player.bUnassignedData = false;

        foreach (NetworkConnection client in m_Connections) {
            SendToClient(JsonUtility.ToJson(newPlayerMsg), client);
        }
    }

    private void ShowClientList() {
        Debug.Log("[Notice] Connected Clients:");
        foreach (NetworkConnection client in m_Connections) {
            Debug.Log(" - " + client.InternalId + "(InternalId) " + m_clientIDDict[client.InternalId].clientID + "(clientID)");
        }
    }

}