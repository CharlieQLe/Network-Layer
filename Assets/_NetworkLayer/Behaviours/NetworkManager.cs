using System;
using UnityEngine;

namespace NetworkLayer {
    public abstract class NetworkManager : MonoBehaviour {
        public static NetworkManager Singleton { get; private set; }

        /// <summary>
        /// Get the client transport
        /// </summary>
        public ClientTransport Client { get; private set; }

        /// <summary>
        /// Raised when the client tries to connect
        /// </summary>
        public event ClientTransport.AttemptConnectionDelegate OnClientAttemptConnectionEvent;

        /// <summary>
        /// Raised when the client connects
        /// </summary>
        public event ClientTransport.ConnectDelegate OnClientConnectEvent;

        /// <summary>
        /// Raised when the client disconnects
        /// </summary>
        public event ClientTransport.DisconnectDelegate OnClientDisconnectEvent;

        /// <summary>
        /// Raised when the client updates
        /// </summary>
        public event ClientTransport.UpdateDelegate OnClientUpdateEvent;

        /// <summary>
        /// Get the server transport.
        /// </summary>
        public ServerTransport Server { get; private set; }

        /// <summary>
        /// Raised when the server starts.
        /// </summary>
        public event ServerTransport.HostDelegate OnServerHostEvent;

        /// <summary>
        /// Raised when the server closes
        /// </summary>
        public event ServerTransport.CloseDelegate OnServerCloseEvent;

        /// <summary>
        /// Raised when a client connects to the server.
        /// </summary>
        public event ServerTransport.ConnectDelegate OnServerConnectEvent;

        /// <summary>
        /// Raised when a client disconnects from the server.
        /// </summary>
        public event ServerTransport.DisconnectDelegate OnServerDisconnectEvent;

        /// <summary>
        /// Raised when the server updates.
        /// </summary>
        public event ServerTransport.UpdateDelegate OnServerUpdateEvent;

#if UNITY_EDITOR

        [NonSerialized] public bool debugClientFoldout;
        [NonSerialized] public bool debugServerFoldout;

#endif

        private void Awake() {
            if (Singleton) {
                Destroy(this);
                return;
            }

            Singleton = this;
            DontDestroyOnLoad(this);
            Client = InitializeClient();
            Client.OnAttemptConnectionEvent += () => {
                OnClientAttemptConnection();
                OnClientAttemptConnectionEvent?.Invoke();
            };
            Client.OnConnectEvent += () => {
                OnClientConnect();
                OnClientConnectEvent?.Invoke();
            };
            Client.OnDisconnectEvent += () => {
                OnClientDisconnect();
                OnClientDisconnectEvent?.Invoke();
            };
            Client.OnUpdateEvent += () => {
                OnClientUpdate();
                OnClientUpdateEvent?.Invoke();
            };
            Client.OnLogEvent += OnLog;

            Server = InitializeServer();
            Server.OnHostEvent += () => {
                OnServerHost();
                OnServerHostEvent?.Invoke();
            };
            Server.OnCloseEvent += () => {
                OnServerClose();
                OnServerCloseEvent?.Invoke();
            };
            Server.OnConnectEvent += clientId => {
                OnServerConnect(clientId);
                OnServerConnectEvent?.Invoke(clientId);
            };
            Server.OnDisconnectEvent += (clientId) => {
                OnServerDisconnect(clientId);
                OnServerDisconnectEvent?.Invoke(clientId);
            };
            Server.OnUpdateEvent += () => {
                OnServerUpdate();
                OnServerUpdateEvent?.Invoke();
            };
            Server.OnLogEvent += OnLog;
        }

        private void FixedUpdate() {
            Server.Update();
            Client.Update();
        }

        private void OnDestroy() {
            if (ReferenceEquals(Singleton, this)) {
                Singleton = null;
                Client.Dispose();
                Server.Dispose();
                OnSingletonDestroy();
            }
        }

        /// <summary>
        /// Invoked if this object is the singleton
        /// </summary>
        protected virtual void OnSingletonDestroy() { }

        /// <summary>
        /// Initialize the client transport.
        /// </summary>
        /// <returns></returns>
        protected abstract ClientTransport InitializeClient();

        /// <summary>
        /// Initialize the server transport.
        /// </summary>
        /// <returns></returns>
        protected abstract ServerTransport InitializeServer();

        protected abstract void OnLog(object message);
        protected abstract void OnClientAttemptConnection();
        protected abstract void OnClientConnect();
        protected abstract void OnClientDisconnect();
        protected abstract void OnClientUpdate();
        protected abstract void OnServerHost();
        protected abstract void OnServerClose();
        protected abstract void OnServerConnect(ulong clientId);
        protected abstract void OnServerDisconnect(ulong clientId);
        protected abstract void OnServerUpdate();
    }
}