using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NetworkLayer.Inspector {
    [CustomEditor(typeof(ClientManager), true)]
    public class ClientInspector : Editor {
        private ClientManager _client;

        private void OnEnable() {
            _client = target as ClientManager;
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            if (!Application.isPlaying) {
                return;
            } else if (_client == null) {
                return;
            } else if (_client.Transport == null) {
                return;
            }

            _client.debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_client.debugFoldout, "Stats");
            if (_client.debugFoldout) {
                EditorGUI.indentLevel++;
                EditorGUILayout.EnumPopup("Connection State", _client.Transport.State);
                EditorGUILayout.IntField("Round Trip Time", _client.Transport.Rtt);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}