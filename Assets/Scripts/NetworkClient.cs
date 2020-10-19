using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

public class NetworkClient : MonoBehaviour{

    [SerializeField] private GameObject clientCubePrefab;
    [SerializeField] private Transform localClientCharacterRef;
    [SerializeField] private Transform spawnLocation;

    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;
    public bool bVerboseDebug = false;

    private Dictionary<ushort, Transform> m_clientIDDict;

    private float m_clientUpdateInterval = 0.1f; //TODO change to 0.033f
    private uint m_pingInterval = 20;
    private ushort m_thisClientID = 0;
    private bool bPingServer = true;
    private bool bUpdateServer = true;

    void Start (){
        Debug.Log("[Notice] Connecting to server...");

        m_clientIDDict = new Dictionary<ushort, Transform>();
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort); // Establish server endpoint

        //var endpoint = NetworkEndPoint.LoopbackIpv4; //sets endpoint to local machine (127.0.0.1)
        //endpoint.Port = serverPort;

        m_Connection = m_Driver.Connect(endpoint);
    }

    public void OnDestroy(){
        Debug.Log("[Notice] Cleaning up network driver...");
        m_Driver.Dispose();
    } 
    
    private void Update(){
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated){
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty){
            if (cmd == NetworkEvent.Type.Connect){
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data){
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect){
                OnDisconnect();
            }
            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }

    private void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    private void OnConnect(){
        GameObject newObj;
        HandshakeMsg handshakeMsg;

        Debug.Log("[Notice] Client connected to server.");

        Debug.Log("[Notice] Routinely pinging server...");
        StartCoroutine(PingServerRoutine(m_pingInterval));

        //Spawn Local Client's own Character
        if (clientCubePrefab) {
            Debug.Log("[Notice] Deploying player character...");

            if (spawnLocation) {
                spawnLocation.position = new Vector3(UnityEngine.Random.Range(-4.0f, 4.0f), 6, UnityEngine.Random.Range(-4.0f, 4.0f)); //Randomize spawn
                newObj = Instantiate(clientCubePrefab, spawnLocation.position, Quaternion.identity);
            }
            else {
                Debug.LogWarning("[Warning] Spawn location reference missing! Spawning player at (0,6,0) instead...");
                newObj = Instantiate(clientCubePrefab, new Vector3(0,6,0), Quaternion.identity);
            }
            newObj.AddComponent<SimpleCharController>();
            localClientCharacterRef = newObj.transform; //Save local char reference. 

            //Note: At this point, the local player character isn't added to the dictionary m_clientIDDict yet. 
            //This is because the server has to assign it its client ID and that is required as the dictionary key. This will be received in a Commands.SERVER_HANDSHAKE.
        }
        else {
            Debug.LogError("[Error] Player character prefab missing! Aborting operation...");
        }
        
        Debug.Log("[Notice] Sending handshake to server...");

        //Create handshake object and designate it as Commands.CLIENT_HANDSHAKE
        handshakeMsg = new HandshakeMsg(Commands.CLIENT_HANDSHAKE);

        //Setting up player data in message
        handshakeMsg.player.cubePosition = localClientCharacterRef.position;
        handshakeMsg.player.cubeOrientation = localClientCharacterRef.eulerAngles;
        handshakeMsg.player.cubeColor = Color.white;
        handshakeMsg.player.bUnassignedData = false;

        //Send handshake to server
        SendToServer(JsonUtility.ToJson(handshakeMsg));

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = m_Connection.InternalId.ToString();
        // SendToServer(JsonUtility.ToJson(m));
    }

    private void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("[Notice] Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE: //shouldnt happen
                Debug.LogError("[Error] Player update message received! Client shouldn't receive messages from clients.");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("[Routine] Server update message received!");


                break;
            case Commands.PONG:
                Debug.Log("[Routine] Pong message received!");
                break;
            case Commands.SERVER_HANDSHAKE:
                ServerHandshakeMsg shMsg = JsonUtility.FromJson<ServerHandshakeMsg>(recMsg);
                Debug.Log("[Notice] Handshake from server received!");

                m_clientIDDict.Add(shMsg.clientID, localClientCharacterRef); //Add local character to player dictionary
                m_thisClientID = shMsg.clientID; //keep a reference to local player ID
                SpawnRemotePlayers(shMsg.players); //Spawn remote players
                StartCoroutine(UploadClientDataRoutine(m_clientUpdateInterval)); //Start routinely updating server with local cube character data
                break;
            case Commands.NEW_PLAYER:
                PlayerUpdateMsg puMSg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("[Notice] A new player has connected. (" + puMSg.player.clientID + ")");
                SpawnRemotePlayer(puMSg.player);
                break;
            case Commands.PLAYER_DISCONNECT:
                PlayerIDMsg pdMSg = JsonUtility.FromJson<PlayerIDMsg>(recMsg);
                Debug.Log("[Notice] A player has disconnected. (" + pdMSg.clientID + ")");
                RemoveRemotePlayer(pdMSg.clientID);
                break;
            default:
                Debug.LogError("[Error] Unrecognized message received!");
                break;
        }
    }

    private void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    private void OnDisconnect(){
        Debug.Log("[Notice] Client got disconnected from server.");
        m_Connection = default(NetworkConnection);
    }

    private void SpawnRemotePlayers(List<NetworkObjects.NetworkPlayer> remotePlayers) {
        GameObject newObj;

        Debug.Log("[Notice] Spawning remote players:");

        if (!clientCubePrefab) {
            Debug.LogError("[Error] Player character prefab missing! Aborting operation...");
        }

        //Spawn remote players
        foreach (NetworkObjects.NetworkPlayer player in remotePlayers) {

            if (!m_clientIDDict.ContainsKey(player.clientID)) { //Don't add players that are already there
                newObj = Instantiate(clientCubePrefab, player.cubePosition, Quaternion.identity);
                newObj.transform.eulerAngles = player.cubeOrientation;
                m_clientIDDict.Add(player.clientID, newObj.transform); //Add to player dictionary
                Debug.Log(" - Client ID: " + player.clientID + " Spawned.");
            }
        }
    }

    private void SpawnRemotePlayer(NetworkObjects.NetworkPlayer remotePlayer) {
        GameObject newObj;
        newObj = Instantiate(clientCubePrefab, remotePlayer.cubePosition, Quaternion.identity);
        newObj.transform.eulerAngles = remotePlayer.cubeOrientation;
        m_clientIDDict.Add(remotePlayer.clientID, newObj.transform); //Add to player dictionary
        Debug.Log("[Notice] Spawned remote player: (" + remotePlayer.clientID + ")");
    }

    private void UpdateRemotePlayers(List<NetworkObjects.NetworkPlayer> remotePlayers) {

        if (bVerboseDebug) {
            Debug.Log("[Routine] Updating remote players with new data...");
        }

        foreach (NetworkObjects.NetworkPlayer updateData in remotePlayers) {
            if (m_clientIDDict.ContainsKey(updateData.clientID)) {
                m_clientIDDict[updateData.clientID].position = updateData.cubePosition;
                m_clientIDDict[updateData.clientID].eulerAngles = updateData.cubeOrientation;
            }
        }

    }

    private void RemoveRemotePlayer(ushort subjectClientID) {

        Debug.Log("[Notice] Removing disconnected player from the game...");

        if (m_clientIDDict.ContainsKey(subjectClientID)) {
            Destroy(m_clientIDDict[subjectClientID].gameObject);
            m_clientIDDict.Remove(subjectClientID);
        }
        else {
            Debug.LogWarning("[Warning] Given key subjectClientID doesn't exist in m_clientIDDict. Aborting operation...");
        }
    }

    private IEnumerator PingServerRoutine(float timeInterval){
        NetworkHeader pingMsg = new NetworkHeader(); 
        pingMsg.cmd = Commands.PING;

        while (bPingServer) {
            if (bVerboseDebug) {
                Debug.Log("[Routine] Pinging server.");
            }
            SendToServer(JsonUtility.ToJson(pingMsg));
            yield return new WaitForSeconds(timeInterval);
        }
    }

    private IEnumerator UploadClientDataRoutine(float timeInterval) {
        PlayerUpdateMsg updateMsg = new PlayerUpdateMsg();

        if (m_thisClientID != 0) {
            
            while (bUpdateServer) {
                if (bVerboseDebug) {
                    Debug.Log("[Routine] Updating server with local character data.");
                }

                //Inserting player data in message
                updateMsg.player.clientID = m_thisClientID;
                updateMsg.player.cubePosition = m_clientIDDict[m_thisClientID].position;
                updateMsg.player.cubeOrientation = m_clientIDDict[m_thisClientID].eulerAngles;
                updateMsg.player.cubeColor = Color.white;
                updateMsg.player.bUnassignedData = false;

                //Sending message
                SendToServer(JsonUtility.ToJson(updateMsg));

                yield return new WaitForSeconds(timeInterval);
            }
        }
        else {
            Debug.LogError("[Error] local client ID unassigned (0). Aborting operation...");
        }
    }
}