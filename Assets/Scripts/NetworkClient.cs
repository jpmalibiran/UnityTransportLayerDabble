using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

//Note: The connections server-to-client and client-to-server are NOT the same connection.
//The internalId represents the connections to an application. A client application may only have one connection- the server and that may be represented by the id 0.
//Because of this the internalId cannot be used as an identifier of separate clients.

public class NetworkClient : MonoBehaviour{

    [SerializeField] private GameObject clientCubePrefab;
    [SerializeField] private Transform localClientCharacterRef;
    [SerializeField] private Transform spawnLocation;

    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;
    public bool bVerboseDebug = false;

    private Dictionary<int, Transform> m_clientIDDict;

    private float m_clientUpdateInterval = 1; //TODO change to 0.033f
    private uint m_pingInterval = 20;
    private ushort m_thisClientID = 0;
    private bool bPingServer = true;

    void Start (){
        Debug.Log("[Notice] Connecting to server...");

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
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.LogError("[Error] Player update message received! Client shouldn't receive messages from clients.");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("[Notice] Server update message received!");
                break;
            case Commands.PONG:
                Debug.Log("[Notice] Pong message received!");
                break;
            case Commands.SERVER_HANDSHAKE:
                ServerHandshakeMsg shMsg = JsonUtility.FromJson<ServerHandshakeMsg>(recMsg);
                Debug.Log("[Notice] Handshake from server received!");

                m_clientIDDict.Add(shMsg.clientID, localClientCharacterRef); //Add local character to player dictionary
                m_thisClientID = shMsg.clientID; //keep a reference to local player ID
                SpawnRemotePlayers(shMsg.players); //Spawn remote players
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

        if (!clientCubePrefab) {
            Debug.LogError("[Error] Player character prefab missing! Aborting operation...");
        }

        //Spawn remote players
        foreach (NetworkObjects.NetworkPlayer player in remotePlayers) {
            newObj = Instantiate(clientCubePrefab, player.cubePosition, Quaternion.identity);
            newObj.transform.eulerAngles = player.cubeOrientation;
            m_clientIDDict.Add(player.clientID, newObj.transform); //Add to player dictionary
        }
    }


    private void SpawnRemotePlayer(NetworkObjects.NetworkPlayer remotePlayer) {
        GameObject newObj;
        newObj = Instantiate(clientCubePrefab, remotePlayer.cubePosition, Quaternion.identity);
        newObj.transform.eulerAngles = remotePlayer.cubeOrientation;
        m_clientIDDict.Add(remotePlayer.clientID, newObj.transform); //Add to player dictionary
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

    //TODO
    private IEnumerator UploadClientDataRoutine(float timeInterval) {

        yield return new WaitForSeconds(timeInterval);
    }
}