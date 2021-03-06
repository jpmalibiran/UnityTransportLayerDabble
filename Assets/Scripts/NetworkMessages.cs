﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Networking.Transport;

namespace NetworkMessages{

    public enum Commands{
        DEFAULT,
        NEW_PLAYER,
        PLAYER_DISCONNECT,
        PLAYER_UPDATE,
        SERVER_UPDATE,
        HANDSHAKE,
        CLIENT_HANDSHAKE,
        SERVER_HANDSHAKE,
        PLAYER_INPUT,
        PING,
        PONG,

    }

    [System.Serializable]
    public class NetworkHeader{
        public Commands cmd;
    }

    [System.Serializable]
    public class PlayerIDMsg: NetworkHeader{
        public ushort clientID;

        public PlayerIDMsg(Commands getCommand) {
            clientID = 0;
            cmd = getCommand;
        }
    }

    [System.Serializable]
    public class ServerHandshakeMsg:NetworkHeader{
        public List<NetworkObjects.NetworkPlayer> players;
        public ushort clientID;

        public ServerHandshakeMsg(ushort setClientID) {
            clientID = setClientID;
            cmd = Commands.SERVER_HANDSHAKE;
            players = new List<NetworkObjects.NetworkPlayer>();
        }
    }

    [System.Serializable]
    public class HandshakeMsg:NetworkHeader{
        public NetworkObjects.NetworkPlayer player;

        public HandshakeMsg(){      // Constructor
            cmd = Commands.HANDSHAKE;
            player = new NetworkObjects.NetworkPlayer();
        }
        public HandshakeMsg(Commands getCommand){      // Constructor
            cmd = getCommand;
            player = new NetworkObjects.NetworkPlayer();
        }
    }
    
    [System.Serializable]
    public class PlayerUpdateMsg:NetworkHeader{
        public NetworkObjects.NetworkPlayer player;
        public PlayerUpdateMsg(){      // Constructor
            cmd = Commands.PLAYER_UPDATE;
            player = new NetworkObjects.NetworkPlayer();
        }
        public PlayerUpdateMsg(Commands getCommand){      // Constructor
            cmd = getCommand;
            player = new NetworkObjects.NetworkPlayer();
        }
    };

    public class PlayerInputMsg:NetworkHeader{
        public Input myInput;
        public PlayerInputMsg(){
            cmd = Commands.PLAYER_INPUT;
            myInput = new Input();
        }
    }
    [System.Serializable]
    public class  ServerUpdateMsg:NetworkHeader{
        public List<NetworkObjects.NetworkPlayer> players;
        public ServerUpdateMsg(){      // Constructor
            cmd = Commands.SERVER_UPDATE;
            players = new List<NetworkObjects.NetworkPlayer>();
        }
    }
} 

namespace NetworkObjects{
    [System.Serializable]
    public class NetworkObject{
        //public string id;
        public ushort clientID;
    }

    [System.Serializable]
    public class NetworkPlayer : NetworkObject{
        public Color cubeColor;
        public Vector3 cubePosition;
        public Vector3 cubeOrientation;
        public bool bUnassignedData;

        public NetworkPlayer(){
            cubeColor = new Color();
            bUnassignedData = true;
            clientID = 0;
        }
    }
}
