using System;
using UnityEngine;
using UnityEngine.Events;

namespace NetworkLayer {
    public abstract class ClientManager<T> : MonoBehaviour where T : ClientManager<T> {
        public static T Singleton { get; private set; }

        [SerializeField] private UnityEvent onAttemptConnection;
        [SerializeField] private UnityEvent onConnect;
        [SerializeField] private UnityEvent onDisconnect;
        [SerializeField] private UnityEvent onUpdate;
        [SerializeField] private UnityEvent<object> onLog;

        public event ClientTransport.AttemptConnectionDelegate OnAttemptConnectionEvent;
        public event ClientTransport.ConnectDelegate OnConnectEvent;
        public event ClientTransport.DisconnectDelegate OnDisconnectEvent;
        public event ClientTransport.UpdateDelegate OnUpdateEvent;
        public event ClientTransport.LogDelegate OnLogEvent;
        
        public ClientTransport Transport { get; private set; }
        
        private void Awake() {
            if (Singleton) {
                Destroy(this);
                return;
            }
            Singleton = (T) this;
            Transport = GetTransport();
            Transport.OnAttemptConnectionEvent += () => {
                OnAttemptConnection();
                onAttemptConnection.Invoke();
                OnAttemptConnectionEvent?.Invoke();
            };
            Transport.OnConnectEvent += () => {
                OnConnect();
                onConnect.Invoke();
                OnConnectEvent?.Invoke();
            };
            Transport.OnDisconnectEvent += () => {
                OnDisconnect();
                onDisconnect.Invoke();
                OnDisconnectEvent?.Invoke();
            };
            Transport.OnUpdateEvent += () => {
                OnUpdate();
                onUpdate.Invoke();
                OnUpdateEvent?.Invoke();
            };
            Transport.OnLogEvent += (message) => {
                OnLog(message);
                onLog.Invoke(message);
                OnLogEvent?.Invoke(message);
            };
        }

        private void OnDestroy() {
            if (ReferenceEquals(Singleton, this)) {
                Singleton = null;
                Transport.Dispose();
                OnDestroying();
            }
        }

        private void FixedUpdate() {
            Transport.Update();
        }

        protected virtual void OnDestroying() { }
        
        protected abstract ClientTransport GetTransport();

        protected abstract void OnAttemptConnection();
        protected abstract void OnConnect();
        protected abstract void OnDisconnect();
        protected abstract void OnUpdate();
        protected abstract void OnLog(object message);
    }
}