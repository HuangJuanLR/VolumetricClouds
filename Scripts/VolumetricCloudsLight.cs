using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
// ReSharper disable Unity.PreferAddressByIdToGraphicsParams

namespace VolumetricClouds
{
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    public class VolumetricCloudsLight : MonoBehaviour
    {
        private static List<VolumetricCloudsLight> s_RegisteredLights = new List<VolumetricCloudsLight>();
        
        public int priority = 0;
        
        private Light m_Light;
        
        public static IReadOnlyList<VolumetricCloudsLight> RegisteredLights => s_RegisteredLights;
        public Light Light => m_Light;

        private void OnEnable()
        {
            m_Light = GetComponent<Light>();

            if (!s_RegisteredLights.Contains(this))
            {
                s_RegisteredLights.Add(this);
            }
        }

        private void OnDisable()
        {
            s_RegisteredLights.Remove(this);
        }

        private void OnDestroy()
        {
            s_RegisteredLights.Remove(this);
        }

        public static List<VolumetricCloudsLight> GetSortedLights(Vector3 referencePoint, int maxCount)
        {
            var result = new List<VolumetricCloudsLight>();

            foreach (var light in s_RegisteredLights)
            {
                if (light != null && light.isActiveAndEnabled && light.m_Light && light.m_Light.enabled)
                {
                    result.Add(light);
                }
            }
            
            result.Sort((a, b) =>
            {
                int priorityCompare = b.priority.CompareTo(a.priority);
                if (priorityCompare != 0) return priorityCompare;

                float distA = Vector3.Distance(a.transform.position, referencePoint);
                float distB = Vector3.Distance(b.transform.position, referencePoint);
                return distA.CompareTo(distB);
            });

            if (result.Count > maxCount)
            {
                result.RemoveRange(maxCount, result.Count - maxCount);
            }
            
            return result;
        }
    }
}
