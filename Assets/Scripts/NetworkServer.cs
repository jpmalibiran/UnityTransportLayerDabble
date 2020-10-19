using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

//Note: The connections server-to-client and client-to-server are NOT the same connection.
//The internalId represents the connections to an application. A client application may only have one connection- the server and that may be represented by the id 0.
//Because of this the internalId cannot be used as an identifier of separate clients. Beware when referencing an item with index.

public class NetworkServer : MonoBehaviour{
    public NetworkDriver m_Driver; //Listens; where the network begins and ends
    public ushort serverPort; // server port
    [SerializeField] private NativeList<NetworkConnection> m_Connections; //List of connections. NativeList is an unmanaged memory list data structure (no garbage collection)

    [SerializeField] private Dictionary<int, NetworkObjects.NetworkPlayer> m_clientIDDict; //the int key represents NetworkConnection.internalId
    [SerializeField] private ushort clientIDCounter = 0;

    private float m_serverUpdateInterval = 0.1f; //TODO change to 0.033f
    private bool bRoutinelyUpdateClients = true;

    private void Start (){
        Debug.Log("[Notice] Setting up server...");

        m_clientIDDict = new Dictionary<int, NetworkObjects.NetworkPlayer>();
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

        StartCoroutine(UpdateClientsRoutine(m_serverUpdateInterval));
    }

    private void Update (){
        m_Driver.ScheduleUpdate().Complete(); //Tells the driver we're ready to listen for the next event

        CleanUpConnections();
        AcceptNewConnections();
        ReadIncomingMessages();
    }

    //Cleans up driver and the unmanaged connection list. Unity or C# doesn't clean it; gotta be disposed manually 
    public void OnDestroy(){
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    //Adds network connection to our list of connections
    private void OnConnect(NetworkConnection c){
        NetworkObjects.NetworkPlayer newClient;

        Debug.Log("[Notice] New client connected to server. (" + c.InternalId + ")");

        m_Connections.Add(c);
        newClient = new NetworkObjects.NetworkPlayer();

        newClient.clientID = GetNewClientID(); //Assign new ID
        newClient.bUnassignedData = true;
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
                //This contrasts from NativeList.RemoveAt() in that NativeList.RemoveAtSwapBack() doesn't retain the order of elements. Ergo, it's faster.
                m_Connections.RemoveAtSwapBack(i); 
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
                //Debug.Log("[Routine] Player update message received!");

                //Update player data
                if (m_clientIDDict.ContainsKey(m_Connections[i].InternalId)) {
                    m_clientIDDict[m_Connections[i].InternalId].cubePosition = puMsg.player.cubePosition;
                    m_clientIDDict[m_Connections[i].InternalId].cubeOrientation = puMsg.player.cubeOrientation;
                }
                else {
                    Debug.LogError("[Error] Given InternalId is not a key in m_clientIDDict; Cannot update player data.");
                }

                break;
            case Commands.SERVER_UPDATE: //TODO: remove this. This is not needed because the server isn't going to send a message to itself
                Debug.LogError("[Error] Server update message received! The server shouldn't receive Commands.SERVER_UPDATE.");
                break;
            case Commands.PING:
                if (m_clientIDDict.ContainsKey(m_Connections[i].InternalId)) {
                    //Debug.Log("[Routine] Ping received from " + m_Connections[i].InternalId + " (InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + " (clientID)");
                    PongClientResponse(i); // Send back Pong message
                }
                else {
                    Debug.LogError("[Error] Ping received, but given InternalId (" + m_Connections[i].InternalId + ") is not a key in m_clientIDDict. Aborting Pong response...");
                }
                break;
            case Commands.CLIENT_HANDSHAKE:
                HandshakeMsg chsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg); 

                if (m_clientIDDict.ContainsKey(m_Connections[i].InternalId)) {
                    Debug.Log("[Notice] Handshake from client received! " + m_Connections[i].InternalId + " (InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + " (clientID)");

                    //Update client player data
                    m_clientIDDict[m_Connections[i].InternalId].cubePosition = chsMsg.player.cubePosition;
                    m_clientIDDict[m_Connections[i].InternalId].cubeOrientation = chsMsg.player.cubeOrientation;
                    m_clientIDDict[m_Connections[i].InternalId].cubeColor = Color.white;
                    m_clientIDDict[m_Connections[i].InternalId].bUnassignedData = false;

                    Debug.Log("clientID: " + m_clientIDDict[m_Connections[i].InternalId].clientID);

                    SendServerHandshake(i, m_clientIDDict[m_Connections[i].InternalId].clientID); //Send back handshake
                    UpdateClientsWithNewPlayer( i); //Send all connected clients the new player data
                }
                else {
                    Debug.LogError("[Error] Handshake received, but given InternalId (" + m_Connections[i].InternalId + ") is not a key in m_clientIDDict. Aborting handshake response...");
                }
                break;
            default:
                Debug.LogError("[Error] SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    //Turns the connection at the index into a default connections. Which will be cleaned up in Update()
    private void OnDisconnect(int i){
        Debug.Log("[Notice] Client disconnected from server. " + m_Connections[i].InternalId + " (InternalId) " + m_clientIDDict[m_Connections[i].InternalId].clientID + " (clientID)");

        //Remove entry in Dictionary m_clientIDDict
        if (m_clientIDDict.ContainsKey(m_Connections[i].InternalId)) {
            UpdateClientsWithDisconnectedPlayer(m_clientIDDict[m_Connections[i].InternalId].clientID);
            m_clientIDDict.Remove(m_Connections[i].InternalId);
        }
        else {
            Debug.LogError("[Error] Given InternalId is not a key in m_clientIDDict.");
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
        if (clientIDCounter == 0) {
            clientIDCounter = 1;
        }

        return clientIDCounter;
    }

    private void PongClientResponse(int getConnectionIndex) {
        NetworkHeader pongMsg = new NetworkHeader(); 
        pongMsg.cmd = Commands.PONG;
        SendToClient(JsonUtility.ToJson(pongMsg), m_Connections[getConnectionIndex]);    

        if (m_clientIDDict.ContainsKey(m_Connections[getConnectionIndex].InternalId)) {
            Debug.Log("[Notice] Pong sent to Client! " + m_Connections[getConnectionIndex].InternalId + " (InternalId) " + m_clientIDDict[m_Connections[getConnectionIndex].InternalId].clientID + " (clientID)");
        }
        else {
            Debug.LogWarning("[Warning] Pong sent to Client, but given InternalId is not a key in m_clientIDDict.");
        }
        
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
        
        if (m_clientIDDict.ContainsKey(m_Connections[targetClientIndex].InternalId)) {
            Debug.Log("[Notice] Handshake sent to Client! " + m_Connections[targetClientIndex].InternalId + " (InternalId) " + m_clientIDDict[m_Connections[targetClientIndex].InternalId].clientID + " (clientID)");
        }
        else {
            Debug.LogWarning("[Warning] Handshake sent to Client, but given InternalId is not a key in m_clientIDDict.");
        }


        ShowClientList();
    }

    private void UpdateClientsWithNewPlayer(int newClientIndex) {
        PlayerUpdateMsg newPlayerMsg;
        int targetClientDictKey = m_Connections[newClientIndex].InternalId;

        Debug.Log("[Notice] Sending new player data to all connected clients...");

        newPlayerMsg = new PlayerUpdateMsg(Commands.NEW_PLAYER);

        if (m_clientIDDict.ContainsKey(targetClientDictKey)) {
            if (!m_clientIDDict[targetClientDictKey].bUnassignedData) {
                newPlayerMsg.player.clientID = m_clientIDDict[targetClientDictKey].clientID;
                newPlayerMsg.player.cubePosition = m_clientIDDict[targetClientDictKey].cubePosition;
                newPlayerMsg.player.cubeOrientation = m_clientIDDict[targetClientDictKey].cubeOrientation;
                newPlayerMsg.player.cubeColor = Color.white;
                newPlayerMsg.player.bUnassignedData = false;
            }
            else {
                Debug.LogError("[Error] New player data unassigned and therefore cannot be sent out to connected clients. Aborting operration...");
            }
        }
        else {
            newPlayerMsg.player.bUnassignedData = true;
            Debug.LogError("[Error] The given client key does not exist in m_clientIDDict.");
        }

        foreach (NetworkConnection client in m_Connections) {

            if (client.InternalId != targetClientDictKey) { //Exclude new player
                SendToClient(JsonUtility.ToJson(newPlayerMsg), client);
            }
        }
    }

    private void UpdateClientsWithAllClientData() {
        ServerUpdateMsg updateMsg = new ServerUpdateMsg();

        //Gather all player data into a network message
        foreach(KeyValuePair<int, NetworkObjects.NetworkPlayer> client in m_clientIDDict){
            updateMsg.players.Add(client.Value);
        }

        //Send data to each connected player
        foreach (NetworkConnection client in m_Connections) {
            SendToClient(JsonUtility.ToJson(updateMsg), client);
        }
    }

    private void UpdateClientsWithDisconnectedPlayer(ushort subjectClientID) {
        PlayerIDMsg disconnectedPlayerMsg;

        Debug.Log("[Notice] Sending disconnected player data to all connected clients...");

        disconnectedPlayerMsg = new PlayerIDMsg(Commands.PLAYER_DISCONNECT);
        disconnectedPlayerMsg.clientID = subjectClientID;

        foreach (NetworkConnection client in m_Connections) {
            SendToClient(JsonUtility.ToJson(disconnectedPlayerMsg), client);
        }
    }

    private void ShowClientList() {
        Debug.Log("[Notice] Connected Clients:");
        foreach (NetworkConnection client in m_Connections) {
            if (m_clientIDDict.ContainsKey(client.InternalId)) {
                Debug.Log(" - " + client.InternalId + " (InternalId) " + m_clientIDDict[client.InternalId].clientID + " (clientID)");
            }
            else {
                Debug.LogWarning(" - " + client.InternalId + " (InternalId) [Warning] Missing key in m_clientIDDict.");
            }
        }
    }

    private IEnumerator UpdateClientsRoutine(float timeInterval){
        
        while (bRoutinelyUpdateClients) {
            if (m_Connections.Length > 0 && m_clientIDDict.Count > 0) {
                UpdateClientsWithAllClientData();
            }
            yield return new WaitForSeconds(timeInterval);
        }
    }

}