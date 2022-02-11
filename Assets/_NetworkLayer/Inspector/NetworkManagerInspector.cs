using UnityEditor;
using UnityEngine;

namespace NetworkLayer.Inspector {
    [CustomEditor(typeof(NetworkManager), true)]
    public class NetworkManagerInspector : Editor {
        private NetworkManager _manager;

        private void OnEnable() {
            _manager = target as NetworkManager;
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Network Info", EditorStyles.boldLabel);
            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("Editor is not in play mode!", MessageType.Warning);
                return;
            }
            if (_manager == null) {
                EditorGUILayout.HelpBox("NetworkManager was not found!", MessageType.Error);
                return;
            }

            if (_manager.Client == null) {
                EditorGUILayout.HelpBox("Client transport was not found!", MessageType.Error);
            } else {
                _manager.debugClientFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_manager.debugClientFoldout, "Client");
                if (_manager.debugClientFoldout) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.TextField("Transport Type", _manager.Client.GetType().Name);
                    EditorGUILayout.EnumPopup("Connection State", _manager.Client.State);
                    if (_manager.Client.State == EClientState.Connected) EditorGUILayout.IntField("Round Trip Time", _manager.Client.Rtt);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (_manager.Server == null) {
                EditorGUILayout.HelpBox("Server transport was not found!", MessageType.Error);
            } else {
                _manager.debugServerFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_manager.debugServerFoldout, "Server");
                if (_manager.debugServerFoldout) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.TextField("Transport Type", _manager.Server.GetType().Name);
                    EditorGUILayout.Toggle("Is Running", _manager.Server.IsRunning);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();  
            }
        }
    }
}