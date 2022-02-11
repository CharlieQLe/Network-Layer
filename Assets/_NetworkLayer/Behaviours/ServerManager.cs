using System;
using UnityEngine;

namespace NetworkLayer {
    public abstract class ServerManager : MonoBehaviour {
        /// <summary>
        /// The server instance.
        /// </summary>
        public static ServerManager Singleton { get; private set; }

        /// <summary>
        /// Raised when the server starts.
        /// </summary>
        public event ServerTransport.HostDelegate OnHostEvent;

        /// <summary>
        /// Raised when the server closes
        /// </summary>
        public event ServerTransport.CloseDelegate OnCloseEvent;

        /// <summary>
        /// Raised when a client connects.
        /// </summary>
        public event ServerTransport.ConnectDelegate OnConnectEvent;

        /// <summary>
        /// Raised when a client disconnects.
        /// </summary>
        public event ServerTransport.DisconnectDelegate OnDisconnectEvent;

        /// <summary>
        /// Raised when the server updates.
        /// </summary>
        public event ServerTransport.UpdateDelegate OnUpdateEvent;

        /// <summary>
        /// Raised when the server logs.
        /// </summary>
        public event ServerTransport.LogDelegate OnLogEvent;

        public ServerTransport Transport { get; private set; }

#if UNITY_EDITOR

        [NonSerialized, HideInInspector] public bool debugFoldout;

#endif

        private void Awake() {
            // Handle singleton
            if (Singleton) {
                Destroy(this);
                return;
            }

            DontDestroyOnLoad(this);
            Singleton = this;

            // Set the transport
            Transport = GetTransport();

            // Subscribe events
            Transport.OnHostEvent += () => {
                OnHost();
                OnHostEvent?.Invoke();
            };
            Transport.OnCloseEvent += () => {
                OnClose();
                OnCloseEvent?.Invoke();
            };
            Transport.OnConnectEvent += client => {
                OnConnect(client);
                OnConnectEvent?.Invoke(client);
            };
            Transport.OnDisconnectEvent += client => {
                OnDisconnect(client);
                OnDisconnectEvent?.Invoke(client);
            };
            Transport.OnUpdateEvent += () => {
                OnUpdate();
                OnUpdateEvent?.Invoke();
            };
            Transport.OnLogEvent += message => {
                OnLog(message);
                OnLogEvent?.Invoke(message);
            };
        }

        private void OnDestroy() {
            // Handle singleton
            if (ReferenceEquals(Singleton, this)) {
                Singleton = null;
                Transport.Dispose();
                OnDestroying();
            }
        }

        private void FixedUpdate() => Transport.Update();

        protected virtual void OnDestroying() { }

        protected abstract ServerTransport GetTransport();

        protected abstract void OnHost();
        protected abstract void OnClose();
        protected abstract void OnConnect(ulong client);
        protected abstract void OnDisconnect(ulong client);
        protected abstract void OnUpdate();
        protected abstract void OnLog(object message);
    }
}