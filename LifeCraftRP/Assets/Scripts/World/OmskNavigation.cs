using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace LifeCraft.World
{
    /// <summary>Routes along the roads and paths already included in the Omsk map.</summary>
    internal static class OmskNavigation
    {
        private static Graph graph;
        private static bool loaded;

        internal static bool TryFind(Vector3 here,Vector3 destination,bool driving,out Vector2[] points,
            out float length)
        {
            points = Array.Empty<Vector2>();
            length = 0f;
            if (!loaded)
            {
                loaded = true;
                graph = Load();
            }
            return graph != null && graph.Find(new Vector2(here.x,here.z),
                new Vector2(destination.x,destination.z),driving?2:1,out points,out length);
        }

        private static Graph Load()
        {
            var asset = Resources.Load<TextAsset>("Omsk/navigation");
            if (!asset)
                return null;
            try
            {
                using (var stream = new GZipStream(new MemoryStream(asset.bytes,false),CompressionMode.Decompress))
                using (var input = new BinaryReader(new BufferedStream(stream,32768)))
                {
                    if (input.ReadUInt32() != 0x324E434C)
                        throw new InvalidDataException("Unexpected Omsk routing data version.");
                    int count = input.ReadInt32(), segments = input.ReadInt32();
                    if (count <= 0 || count > 500000 || segments <= 0 || segments > 900000)
                        throw new InvalidDataException("Invalid Omsk routing graph size.");
                    var vertices = new Vector2[count];
                    int x=0, z=0;
                    for (int i=0;i<count;i++)
                    {
                        x+=ReadDelta(input);
                        z+=ReadDelta(input);
                        if (x < -300000 || x > 300000 || z < -300000 || z > 300000)
                            throw new InvalidDataException("Invalid Omsk routing coordinates.");
                        vertices[i]=new Vector2(x*.1f,z*.1f);
                    }
                    var edges = new Segment[segments];
                    int a=0;
                    for (int i=0;i<segments;i++)
                    {
                        a+=ReadPacked(input);
                        int b=a+ReadPacked(input), mode=input.ReadByte();
                        if (a<0 || b<=a || b>=count || mode<1 || mode>15 || vertices[a]==vertices[b])
                            throw new InvalidDataException("Invalid Omsk street segment.");
                        edges[i] = new Segment(a,b,mode,Vector2.Distance(vertices[a],vertices[b]));
                    }
                    return new Graph(vertices,edges);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                Debug.LogWarning("Омск · маршруты недоступны: "+ex.Message);
                return null;
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

        private static int ReadDelta(BinaryReader input)
        {
            int packed=ReadPacked(input);
            return (packed>>1)^-(packed&1);
        }

        private static int ReadPacked(BinaryReader input)
        {
            int value=0;
            for (int shift=0;shift<=14;shift+=7)
            {
                int next=input.ReadByte();
                value|=(next&127)<<shift;
                if (next<128)
                    return value;
            }
            throw new InvalidDataException("Invalid Omsk routing coordinate or index delta.");
        }

        private readonly struct Segment
        {
            internal readonly int A,B,Mode;
            internal readonly float Length;

            internal Segment(int a,int b,int mode,float length)
            {
                A=a; B=b; Mode=mode; Length=length;
            }

            internal bool Forward(int mode) => (Mode & mode)!=0;
            internal bool Backward(int mode) => (Mode & (mode<<2))!=0;
        }

        private readonly struct Snap
        {
            internal readonly int Edge;
            internal readonly Vector2 Point;
            internal readonly float Fraction,Offset;
            internal float Penalty => Offset*2.6f;

            internal Snap(int edge,Vector2 point,float fraction,float offset)
            {
                Edge=edge; Point=point; Fraction=fraction; Offset=offset;
            }
        }

        private readonly struct Goal
        {
            internal readonly float Cost;
            internal readonly Snap End;

            internal Goal(float cost,Snap end)
            {
                Cost=cost; End=end;
            }
        }

        private sealed class Graph
        {
            private const int CellSize = 128;
            private const float MaxSnap = 110f;
            private const float AlternateSnapRange = 24f;
            private const int SnapCount = 10;
            private const int SearchLimit = 190000;
            private readonly Vector2[] vertices;
            private readonly Segment[] edges;
            private readonly int[] adjacency;
            private readonly int[] starts;
            private readonly Dictionary<long,List<int>> cells = new Dictionary<long,List<int>>();
            private readonly int[] edgeVisited;
            private readonly int[] previous, searchVisited, heapIndex;
            private readonly float[] costs;
            private readonly byte[] departure;
            private readonly MinHeap queue;
            private int snapRound, searchRound;
            private Vector2 destination;

            internal Graph(Vector2[] vertices,Segment[] edges)
            {
                this.vertices=vertices;
                this.edges=edges;
                edgeVisited=new int[edges.Length];
                costs=new float[vertices.Length];
                searchVisited=new int[vertices.Length];
                previous=new int[vertices.Length];
                departure=new byte[vertices.Length];
                heapIndex=new int[vertices.Length];
                queue=new MinHeap(heapIndex);
                starts=new int[vertices.Length+1];
                adjacency=new int[edges.Length*2];
                for (int i=0;i<edges.Length;i++)
                {
                    Segment edge = edges[i];
                    starts[edge.A+1]++;
                    starts[edge.B+1]++;
                    IndexSegment(i,vertices[edge.A],vertices[edge.B]);
                }
                for (int i=1;i<starts.Length;i++)
                    starts[i]+=starts[i-1];
                int[] next=(int[])starts.Clone();
                for (int i=0;i<edges.Length;i++)
                {
                    Segment edge=edges[i];
                    adjacency[next[edge.A]++]=i<<1;
                    adjacency[next[edge.B]++]=i<<1|1;
                }
            }

            private static int Cell(float position) => Mathf.FloorToInt(position/CellSize);
            private static long Key(int x,int z) => ((long)x<<32)|(uint)z;

            private void IndexSegment(int edge,Vector2 a,Vector2 b)
            {
                int xmax=Cell(Mathf.Max(a.x,b.x)), zmax=Cell(Mathf.Max(a.y,b.y));
                for (int x=Cell(Mathf.Min(a.x,b.x));x<=xmax;x++)
                for (int z=Cell(Mathf.Min(a.y,b.y));z<=zmax;z++)
                {
                    long key=Key(x,z);
                    if (!cells.TryGetValue(key,out var rows))
                        cells.Add(key,rows=new List<int>());
                    rows.Add(edge);
                }
            }

            private List<Snap> Nearest(Vector2 point,int mode)
            {
                var best=new List<Snap>(SnapCount);
                if (++snapRound == int.MaxValue)
                {
                    Array.Clear(edgeVisited,0,edgeVisited.Length);
                    snapRound=1;
                }
                int xmax=Cell(point.x+MaxSnap), zmax=Cell(point.y+MaxSnap);
                for (int x=Cell(point.x-MaxSnap);x<=xmax;x++)
                for (int z=Cell(point.y-MaxSnap);z<=zmax;z++)
                {
                    if (!cells.TryGetValue(Key(x,z),out var nearby))
                        continue;
                    foreach (int index in nearby)
                    {
                        if (edgeVisited[index] == snapRound)
                            continue;
                        edgeVisited[index]=snapRound;
                        Segment edge=edges[index];
                        if (!edge.Forward(mode) && !edge.Backward(mode))
                            continue;
                        Vector2 a=vertices[edge.A], b=vertices[edge.B], axis=b-a;
                        float fraction=Mathf.Clamp01(Vector2.Dot(point-a,axis)/axis.sqrMagnitude);
                        Vector2 nearest=a+axis*fraction;
                        float offset=Vector2.Distance(point,nearest);
                        if (offset>MaxSnap || (best.Count!=0 && offset>best[0].Offset+AlternateSnapRange))
                            continue;
                        int rank=best.Count;
                        while (rank>0 && best[rank-1].Offset>offset)
                            rank--;
                        if (rank==SnapCount)
                            continue;
                        if (best.Count==SnapCount)
                            best.RemoveAt(SnapCount-1);
                        best.Insert(rank,new Snap(index,nearest,fraction,offset));
                    }
                }
                while (best.Count>1 && best[best.Count-1].Offset>best[0].Offset+AlternateSnapRange)
                    best.RemoveAt(best.Count-1);
                return best;
            }

            internal bool Find(Vector2 here,Vector2 target,int mode,out Vector2[] points,out float length)
            {
                points=Array.Empty<Vector2>();
                length=0f;
                if (!Finite(here) || !Finite(target) || Mathf.Abs(here.x)>100000 || Mathf.Abs(here.y)>100000 ||
                    Mathf.Abs(target.x)>100000 || Mathf.Abs(target.y)>100000)
                    return false;
                List<Snap> from=Nearest(here,mode), to=Nearest(target,mode);
                if (from.Count==0 || to.Count==0)
                    return false;

                if (++searchRound==int.MaxValue)
                {
                    Array.Clear(searchVisited,0,searchVisited.Length);
                    searchRound=1;
                }
                queue.Clear();
                destination=target;
                var goals=new Dictionary<int,Goal>();
                foreach (Snap end in to)
                {
                    Segment edge=edges[end.Edge];
                    if (edge.Forward(mode) || end.Fraction==0f)
                        AddGoal(goals,edge.A,end,end.Fraction*edge.Length);
                    if (edge.Backward(mode) || end.Fraction==1f)
                        AddGoal(goals,edge.B,end,(1f-end.Fraction)*edge.Length);
                }

                int winner=-1, directStart=-1;
                Snap endPoint=default;
                float best=float.PositiveInfinity;
                for (int i=0;i<from.Count;i++)
                {
                    Snap start=from[i];
                    Segment edge=edges[start.Edge];
                    if (edge.Backward(mode) || start.Fraction==0f)
                        SetCost(edge.A,-1,(byte)i,start.Penalty+start.Fraction*edge.Length);
                    if (edge.Forward(mode) || start.Fraction==1f)
                        SetCost(edge.B,-1,(byte)i,start.Penalty+(1f-start.Fraction)*edge.Length);

                    foreach (Snap end in to)
                    {
                        if (end.Edge!=start.Edge)
                            continue;
                        float along=end.Fraction-start.Fraction;
                        if ((along>0f && !edge.Forward(mode)) || (along<0f && !edge.Backward(mode)))
                            continue;
                        float total=start.Penalty+end.Penalty+Mathf.Abs(along)*edge.Length;
                        if (total<best)
                        {
                            best=total; directStart=i; endPoint=end;
                        }
                    }
                }

                for (int expanded=0;expanded<SearchLimit && queue.Count>0;expanded++)
                {
                    if (queue.Priority>=best)
                        break;
                    int current=queue.Pop();
                    float cost=costs[current];
                    if (goals.TryGetValue(current,out Goal goal) && cost+goal.Cost<best)
                    {
                        best=cost+goal.Cost;
                        winner=current;
                        endPoint=goal.End;
                    }
                    for (int n=starts[current];n<starts[current+1];n++)
                    {
                        int arc=adjacency[n];
                        Segment edge=edges[arc>>1];
                        bool backwards=(arc&1)!=0;
                        if (backwards ? edge.Backward(mode) : edge.Forward(mode))
                            SetCost(backwards?edge.A:edge.B,current,departure[current],cost+edge.Length);
                    }
                }

                if (winner<0)
                {
                    if (directStart<0)
                        return false;
                    points=new[]{from[directStart].Point,endPoint.Point};
                    length=Vector2.Distance(points[0],points[1]);
                    return true;
                }
                int startIndex=departure[winner];
                var result=new List<Vector2> {endPoint.Point};
                for (int n=winner;n>=0;n=previous[n])
                    Append(result,vertices[n]);
                Append(result,from[startIndex].Point);
                if (result.Count==1)
                    result.Add(result[0]);
                result.Reverse();
                points=result.ToArray();
                for (int i=1;i<points.Length;i++)
                    length+=Vector2.Distance(points[i-1],points[i]);
                return true;
            }

            private static bool Finite(Vector2 point) =>
                !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                !float.IsInfinity(point.x) && !float.IsInfinity(point.y);

            private static void Append(List<Vector2> result,Vector2 point)
            {
                if ((result[result.Count-1]-point).sqrMagnitude>.0001f)
                    result.Add(point);
            }

            private static void AddGoal(Dictionary<int,Goal> goals,int node,Snap end,float edgeCost)
            {
                float cost=end.Penalty+edgeCost;
                if (!goals.TryGetValue(node,out Goal existing) || existing.Cost>cost)
                    goals[node]=new Goal(cost,end);
            }

            private void SetCost(int node,int parent,byte source,float distance)
            {
                if (searchVisited[node]!=searchRound)
                {
                    searchVisited[node]=searchRound;
                    heapIndex[node]=-1;
                }
                else if (distance>=costs[node])
                    return;

                previous[node]=parent;
                departure[node]=source;
                costs[node]=distance;
                queue.PushOrUpdate(node,distance+Vector2.Distance(vertices[node],destination));
            }
        }

        private sealed class MinHeap
        {
            private readonly int[] nodes, positions;
            private readonly float[] priorities;
            internal int Count { get; private set; }
            internal float Priority => priorities[0];

            internal MinHeap(int[] positions)
            {
                this.positions=positions;
                nodes=new int[positions.Length];
                priorities=new float[positions.Length];
            }

            internal void Clear() => Count=0;

            internal void PushOrUpdate(int node,float priority)
            {
                int i=positions[node];
                if (i<0)
                    i=Count++;
                while (i>0)
                {
                    int parent=(i-1)/2;
                    if (priorities[parent]<=priority)
                        break;
                    Assign(i,nodes[parent],priorities[parent]);
                    i=parent;
                }
                Assign(i,node,priority);
            }

            internal int Pop()
            {
                int result=nodes[0], last=--Count;
                positions[result]=-1;
                if (last==0)
                    return result;
                int node=nodes[last];
                float priority=priorities[last];
                int i=0;
                while (i*2+1<Count)
                {
                    int child=i*2+1;
                    if (child+1<Count && priorities[child+1]<priorities[child])
                        child++;
                    if (priorities[child]>=priority)
                        break;
                    Assign(i,nodes[child],priorities[child]);
                    i=child;
                }
                Assign(i,node,priority);
                return result;
            }

            private void Assign(int index,int node,float priority)
            {
                nodes[index]=node;
                priorities[index]=priority;
                positions[node]=index;
            }
        }
    }
}
