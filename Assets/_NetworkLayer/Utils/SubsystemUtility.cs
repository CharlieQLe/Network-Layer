using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace NetworkLayer.Utils {
    public static class SubsystemUtility {
        /// <summary>
        /// Insert the subsystem at the front of the subsystem list of the parent.
        /// </summary>
        /// <param name="system"></param>
        /// <param name="parentType"></param>
        public static void InsertAtFront(PlayerLoopSystem system, Type parentType) {
            // Get the root subsystem
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            
            // Iterate through the subsystems of the root subsystem
            for (int i = 0; i < root.subSystemList.Length; i++) {
                // The current subsystem
                PlayerLoopSystem subsystem = root.subSystemList[i];
                
                // Skip if the subsystem is not of type FixedUpdate
                if (subsystem.type != parentType) continue;

                // Get the list of subsystems of the FixedUpdate subsystem
                List<PlayerLoopSystem> systems = new List<PlayerLoopSystem>(subsystem.subSystemList);
                
                // Insert a new subsystem
                systems.Insert(0, system);

                // Set the subsystem list
                subsystem.subSystemList = systems.ToArray();

                // Update the FixedUpdate subsystem
                root.subSystemList[i] = subsystem;
                
                // Exit the loop upon success
                break;
            }
            
            // Set the player loop
            PlayerLoop.SetPlayerLoop(root);
        }
    }
}