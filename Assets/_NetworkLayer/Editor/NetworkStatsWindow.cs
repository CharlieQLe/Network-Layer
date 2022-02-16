using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NetworkLayer.Inspector {
    public class NetworkStatsWindow : EditorWindow {
        private bool _clientStats;
        private bool _serverStats;
        private List<ulong> _serverClients;

        [MenuItem("Network Layer/Statistics")]
        public static void ShowWindow() {
            NetworkStatsWindow window = GetWindow<NetworkStatsWindow>();
            window.titleContent = new GUIContent("Network Layer Statistics");
        }

        private void OnEnable() {
            if (_serverClients == null) _serverClients = new List<ulong>();
        }

        private void OnGUI() {
            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("Statistics only available in play mode!", MessageType.Info);
                return;
            }

            _clientStats = EditorGUILayout.BeginFoldoutHeaderGroup(_clientStats, "Client");
            if (_clientStats) {
                EditorGUI.indentLevel++;
                if (NetworkClient.Transport == null) EditorGUILayout.HelpBox("Transport not found!", MessageType.Error);
                else {
                    EditorGUILayout.TextField("Transport", NetworkClient.Transport.GetType().Name);
                    EditorGUILayout.EnumPopup("Connection State", NetworkClient.Transport.State);
                    if (NetworkClient.Transport.State == EClientState.Connected) {
                        EditorGUILayout.IntField("Round Trip Time", NetworkClient.Transport.Rtt);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            _serverStats = EditorGUILayout.BeginFoldoutHeaderGroup(_serverStats, "Server");
            if (_serverStats) {
                EditorGUI.indentLevel++;
                if (NetworkServer.Transport == null) EditorGUILayout.HelpBox("Transport not found!", MessageType.Error);
                else {
                    EditorGUILayout.TextField("Transport", NetworkServer.Transport.GetType().Name);
                    EditorGUILayout.Toggle("Is Running", NetworkServer.Transport.IsRunning);
                    if (NetworkServer.Transport.IsRunning) {
                        EditorGUILayout.IntField("Client Count", NetworkServer.Transport.ClientCount);
                        NetworkServer.Transport.PopulateClientIds(_serverClients);
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < _serverClients.Count; i++) {
                            EditorGUILayout.LabelField($"Client {_serverClients[i]}");
                            EditorGUI.indentLevel++;
                            EditorGUILayout.IntField("Round Trip Time", NetworkServer.Transport.GetRTT(_serverClients[i]));
                            EditorGUI.indentLevel--;
                        }
                        EditorGUI.indentLevel--;
                        EditorGUILayout.EndFoldoutHeaderGroup();
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}