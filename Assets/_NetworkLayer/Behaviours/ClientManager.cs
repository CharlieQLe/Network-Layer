using System;
using UnityEngine;

namespace NetworkLayer {
    public abstract class ClientManager : MonoBehaviour {
        /// <summary>
        /// The client instance.
        /// </summary>
        public static ClientManager Singleton { get; private set; }

        /// <summary>
        /// Raised when the client tries to connect
        /// </summary>
        public event ClientTransport.AttemptConnectionDelegate OnAttemptConnectionEvent;
        
        /// <summary>
        /// Raised when the client connects
        /// </summary>
        public event ClientTransport.ConnectDelegate OnConnectEvent;
        
        /// <summary>
        /// Raised when the client disconnects
        /// </summary>
        public event ClientTransport.DisconnectDelegate OnDisconnectEvent;
        
        /// <summary>
        /// Raised when the client updates
        /// </summary>
        public event ClientTransport.UpdateDelegate OnUpdateEvent;
        
        /// <summary>
        /// Raised when the client logs
        /// </summary>
        public event ClientTransport.LogDelegate OnLogEvent;

#if UNITY_EDITOR
        [NonSerialized, HideInInspector] public bool debugFoldout;
#endif
        
        public ClientTransport Transport { get; private set; }
        
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
            
            // Subscribe to events
            Transport.OnAttemptConnectionEvent += () => {
                OnAttemptConnection();
                OnAttemptConnectionEvent?.Invoke();
            };
            Transport.OnConnectEvent += () => {
                OnConnect();
                OnConnectEvent?.Invoke();
            };
            Transport.OnDisconnectEvent += () => {
                OnDisconnect();
                OnDisconnectEvent?.Invoke();
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
        
        protected abstract ClientTransport GetTransport();

        protected abstract void OnAttemptConnection();
        protected abstract void OnConnect();
        protected abstract void OnDisconnect();
        protected abstract void OnUpdate();
        protected abstract void OnLog(object message);
    }
}