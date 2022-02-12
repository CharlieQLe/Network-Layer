using System;
using UnityEngine;

namespace NetworkLayer.Examples {
    public abstract class ExampleNetworkManager : NetworkManager {

        [SerializeField] private ETransportType transportType;
        [SerializeField] private string ip = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        protected string Ip => ip;
        protected ushort Port => port;

        protected abstract string GetMessageGroupName();
        protected override ClientTransport InitializeClient() => ExampleUtility.GetClientTransport(transportType, GetMessageGroupName());
        protected override ServerTransport InitializeServer() => ExampleUtility.GetServerTransport(transportType, GetMessageGroupName());
        protected override void OnLog(object message) => Debug.Log(message);
    }
}