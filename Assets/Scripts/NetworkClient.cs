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
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;
    public bool bVerboseDebug = false;

    private bool bPingServer = true;
    private uint m_pingInterval = 20;

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
    
    void Update(){
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

    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("[Notice] Client connected to server.");

        Debug.Log("[Notice] Routinely pinging server...");
        StartCoroutine(PingServerRoutine(m_pingInterval));

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = m_Connection.InternalId.ToString();
        // SendToServer(JsonUtility.ToJson(m));
    }

    void OnData(DataStreamReader stream){
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
            default:
                Debug.LogError("[Error] Unrecognized message received!");
                break;
        }
    }

    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("[Notice] Client got disconnected from server.");
        m_Connection = default(NetworkConnection);
    }

    IEnumerator PingServerRoutine(float timeInterval){
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

}