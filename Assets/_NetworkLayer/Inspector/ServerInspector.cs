using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NetworkLayer.Inspector {
    [CustomEditor(typeof(ServerManager), true)]
    public class ServerInspector : Editor {
        private ServerManager _server;

        private void OnEnable() {
            _server = target as ServerManager;
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            if (!Application.isPlaying) {
                return;
            } else if (_server == null) {
                return;
            } else if (_server.Transport == null) {
                return;
            }

            _server.debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_server.debugFoldout, "Stats");
            if (_server.debugFoldout) {
                EditorGUI.indentLevel++;
                EditorGUILayout.Toggle("Is Running", _server.Transport.IsRunning);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}