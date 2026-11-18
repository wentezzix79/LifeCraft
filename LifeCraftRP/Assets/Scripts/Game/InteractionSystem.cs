using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    public sealed class InteractionSystem
    {
        public sealed class Site
        {
            private readonly Vector3 point;
            private readonly Transform tracked;
            public string Label { get; }
            public Action Use { get; }
            public Vector3 Position => tracked != null ? tracked.position : point;

            public Site(string label, Vector3 position, Action use, Transform target)
            {
                Label = label;
                point = position;
                Use = use;
                tracked = target;
            }
        }

        private readonly List<Site> sites = new List<Site>();

        public Site Add(string label, Vector3 position, Action use, Transform tracked = null)
        {
            var site = new Site(label, position, use, tracked);
            sites.Add(site);
            return site;
        }

        public Site Find(Vector3 origin, Vector3 forward)
        {
            Site closest = null;
            float best = 3.1f * 3.1f;
            foreach (Site site in sites)
            {
                Vector3 offset = site.Position - origin;
                if (Mathf.Abs(offset.y) > 1.8f)
                    continue;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                float score = distance;
                if (distance > 1f && Vector3.Dot(offset.normalized, forward) < -.45f)
                    score += 2f;
                if (score < best && distance < 3.1f * 3.1f)
                {
                    best = score;
                    closest = site;
                }
            }
            return closest;
        }
    }
}
