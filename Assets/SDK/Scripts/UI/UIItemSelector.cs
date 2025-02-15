﻿using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ThunderRoad
{
    public class UIItemSelector : MonoBehaviour
    {
        public Transform spawnPoint;
        public string menuAddress = "Bas.WorldMenu.ItemSelector";
        public float visibleRadius = 4;


        protected void OnDrawGizmos()
        {
            if (spawnPoint) Gizmos.DrawWireSphere(spawnPoint.position, 0.1f);
            Gizmos.matrix = this.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.402f, 0.29f, 0));
        }
    }
}
