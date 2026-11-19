using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LifeCraft.World
{
    internal static class OmskMapCatalog
    {
        [Serializable]
        private sealed class EntryData
        {
            public Point[] points;
        }

        [Serializable]
        internal sealed class Point
        {
            public string id;
            public string name;
            public string detail;
            public string category;
            public float x;
            public float z;

            [NonSerialized] private string searchText;

            public Vector3 Position => new Vector3(x, 0f, z);

            public bool Matches(string query)
            {
                string terms = Normalize(query);
                if (terms.Length == 0)
                    return true;
                if (searchText == null)
                    searchText = Normalize(name + " " + detail);
                foreach (string term in terms.Split(' '))
                    if (searchText.IndexOf(term, StringComparison.Ordinal) < 0)
                        return false;
                return true;
            }

            private static string Normalize(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;
                var normalized = new StringBuilder(value.Length);
                bool separator = false;
                foreach (char character in value)
                {
                    if (char.IsLetterOrDigit(character))
                    {
                        if (separator && normalized.Length > 0)
                            normalized.Append(' ');
                        char lower = char.ToLowerInvariant(character);
                        normalized.Append(lower == 'ё' ? 'е' : lower);
                        separator = false;
                    }
                    else
                        separator = true;
                }
                return normalized.ToString();
            }
        }

        private static IReadOnlyList<Point> points;

        public static IReadOnlyList<Point> Load()
        {
            if (points != null)
                return points;

            TextAsset data = Resources.Load<TextAsset>("Omsk/map-points");
            if (!data)
                return points = Array.Empty<Point>();
            try
            {
                Point[] read = JsonUtility.FromJson<EntryData>(data.text)?.points;
                points = read == null ? Array.Empty<Point>() : Array.AsReadOnly(read);
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning("Омск · каталог меток: " + error.Message);
                points = Array.Empty<Point>();
            }
            finally
            {
                Resources.UnloadAsset(data);
            }
            return points;
        }
    }
}
