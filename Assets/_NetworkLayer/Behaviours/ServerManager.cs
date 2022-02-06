using System;
using UnityEngine;
using UnityEngine.Events;

namespace NetworkLayer {
    public abstract class ServerManager<T> : MonoBehaviour where T : ServerManager<T> {
        public static T Singleton { get; private set; }

        [SerializeField] private UnityEvent onHost;
        [SerializeField] private UnityEvent onClose;
        [SerializeField] private UnityEvent<ulong> onConnect;
        [SerializeField] private UnityEvent<ulong> onDisconnect;
        [SerializeField] private UnityEvent onUpdate;
        [SerializeField] private UnityEvent<object> onLog;

        public event ServerTransport.HostDelegate OnHostEvent;
        public event ServerTransport.CloseDelegate OnCloseEvent;
        public event ServerTransport.ConnectDelegate OnConnectEvent;
        public event ServerTransport.DisconnectDelegate OnDisconnectEvent;
        public event ServerTransport.UpdateDelegate OnUpdateEvent;
        public event ServerTransport.LogDelegate OnLogEvent;
        
        public ServerTransport Transport { get; private set; }

        private void Awake() {
            if (Singleton) {
                Destroy(this);
                return;
            }
            Singleton = (T) this;
            Transport = GetTransport();
            Transport.OnHostEvent += () => {
                OnHost();
                onHost.Invoke();
                OnHostEvent?.Invoke();
            };
            Transport.OnCloseEvent += () => {
                OnClose();
                onClose.Invoke();
                OnCloseEvent?.Invoke();
            };
            Transport.OnConnectEvent += client => {
                OnConnect(client);
                onConnect.Invoke(client);
                OnConnectEvent?.Invoke(client);
            };
            Transport.OnDisconnectEvent += client => {
                OnDisconnect(client);
                onDisconnect.Invoke(client);
                OnDisconnectEvent?.Invoke(client);
            };
            Transport.OnUpdateEvent += () => {
                OnUpdate();
                onUpdate.Invoke();
                OnUpdateEvent?.Invoke();
            };
            Transport.OnLogEvent += message => {
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
        
        protected abstract ServerTransport GetTransport();

        protected abstract void OnHost();
        protected abstract void OnClose();
        protected abstract void OnConnect(ulong client);
        protected abstract void OnDisconnect(ulong client);
        protected abstract void OnUpdate();
        protected abstract void OnLog(object message);
    }
}