using CADability.GeoObject;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Transactions;
using Wintellect.PowerCollections;

namespace CADability
{
    public class ImportSTL
    {
        private StreamReader sr;
        private bool isASCII;
        private BinaryReader br;
        private int numdec = 0, numnum = 0;
        private List<GeoPoint> points = new List<GeoPoint>(); // list of individual, unambiguous points in the STL file. There are no duplicates.
        private OctTree<insertableGeoPoint> pointsOctTree; // OctTree to find identical points fast
        private Dictionary<(int, int), edge> edges = new Dictionary<(int, int), edge>();
        private List<triangle> triangles = new List<triangle>();
        private double STLprecision = 1e-6;
        class triangle
        {
            public int p1, p2, p3;
            public edge e1, e2, e3; // the three edges, p1->p2, p2->p3, p3->p1
            public ISurface surface; // on this surface
            public triangle(int p1, int p2, int p3)
            {
                this.p1 = p1;
                this.p2 = p2;
                this.p3 = p3;
                //e1 = CreateOrFindEdge(p1, p2, this);
                //e2 = new edge(p1, p2);
                //e3 = new edge(p1, p2);
            }
            public GeoVector GetNormal(List<GeoPoint> points)
            {
                return ((points[p2] - points[p1]) ^ (points[p3] - points[p1]));
            }
        }
        /// <summary>
        /// An edge connecting two triangles or the unconnected edge of a triangle
        /// </summary>
        class edge
        {
            public int p1, p2; // the two points of the edge, p1<p2
            public triangle t1, t2; // the two triangles connected by this edge, t2 may be null
            public double bendingAngle; // the angle between the two connected triangles
            public edge(int p1, int p2)
            {
                if (p1 < p2)
                {
                    this.p1 = p1;
                    this.p2 = p2;
                }
                else
                {
                    this.p1 = p2;
                    this.p2 = p1;
                }
            }

            internal triangle OtherTraingle(triangle t)
            {
                if (t == t1) return t2;
                else return t1;
            }
        }
        class insertableGeoPoint : IOctTreeInsertable
        {
            public GeoPoint p;
            public int index = -1;
            public insertableGeoPoint(GeoPoint p)
            {
                this.p = p;
            }

            public BoundingCube GetExtent(double precision)
            {
                return new BoundingCube(p, precision);
            }

            public bool HitTest(ref BoundingCube cube, double precision)
            {
                return cube.Contains(p);
            }

            public bool HitTest(Projection projection, BoundingRect rect, bool onlyInside)
            {
                throw new NotImplementedException();
            }

            public bool HitTest(Projection.PickArea area, bool onlyInside)
            {
                throw new NotImplementedException();
            }

            public double Position(GeoPoint fromHere, GeoVector direction, double precision)
            {
                throw new NotImplementedException();
            }
        }
        public ImportSTL()
        {
        }
        class STLItem
        {
            public GeoPoint p1, p2, p3;
            public UInt16 attr;

            public STLItem(GeoPoint p1, GeoPoint p2, GeoPoint p3, GeoVector normal)
            {
                if (((p2 - p1) ^ (p3 - p2)) * normal > 0)
                {
                    this.p1 = p1;
                    this.p2 = p2;
                    this.p3 = p3;
                }
                else
                {
                    this.p1 = p1;
                    this.p2 = p3;
                    this.p3 = p2;
                }
            }
        }

        private int InsertOrFindPoint(GeoPoint p)
        {
            insertableGeoPoint ip = new insertableGeoPoint(p);
            if (!pointsOctTree.IsEmpty)
            {
                insertableGeoPoint[] closePoints = pointsOctTree.GetObjectsCloseTo(ip);
                for (int i = 0; i < closePoints.Length; i++)
                {
                    if ((closePoints[i].p | p) < STLprecision) return closePoints[i].index;
                }
            }
            ip.index = points.Count;
            points.Add(p);
            pointsOctTree.AddObject(ip);
            return ip.index;
        }
        private (int, int) ek(int a, int b)
        {
            if (a < b) return (a, b);
            else return (b, a);
        }
        private edge CreateOrFindEdge(int p1, int p2, triangle t)
        {
            if (p2 < p1)
            {
                int tmp = p1;
                p1 = p2;
                p2 = tmp;
            }
            if (edges.TryGetValue((p1, p2), out edge edge))
            {
                edge.t2 = t;
                return edge;
            }
            edge = new edge(p1, p2);
            edge.t1 = t;
            edges[(p1, p2)] = edge;
            return edge;
        }
        private void MakeEdges(triangle tr)
        {
            tr.e1 = CreateOrFindEdge(tr.p1, tr.p2, tr);
            tr.e2 = CreateOrFindEdge(tr.p2, tr.p3, tr);
            tr.e3 = CreateOrFindEdge(tr.p3, tr.p1, tr);
        }
        /// <summary>
        /// Calculate the bending angle between the two triangles connected by this edge
        /// </summary>
        /// <param name="edge"></param>
        private void CalcBendingAngle(edge edge)
        {
            if (edge.t1 != null && edge.t2 != null)
            {
                GeoVector n1 = edge.t1.GetNormal(points);
                GeoVector n2 = edge.t2.GetNormal(points);
                SweepAngle sw = new SweepAngle(n1, n2);
                edge.bendingAngle = sw.Radian;
            }
            else
            {
                edge.bendingAngle = Math.PI; // this is the attribute for unconnected edges
            }
        }

        public Shell[] Read(string fileName)
        {
            List<Shell> res = new List<Shell>();

            if (!String.IsNullOrEmpty(fileName) && File.Exists(fileName))
            {
                TriangleReader reader = null;
                if (IsASCII(new StreamReader(fileName)))
                    reader = new TriangleReaderASCII(fileName);
                else
                    reader = new TriangleReaderBinary(fileName);
                res = GetShells(reader);
                reader.Close();
            }
            // simply read all triangles. Orient the triangles to respect the normal
            List<STLItem> items = new List<STLItem>();
            BoundingCube extent = new BoundingCube();
            do
            {
                STLItem item = GetNextSTLItem();
                if (item == null) break;
                items.Add(item);
                extent.MinMax(item.p1);
                extent.MinMax(item.p2);
                extent.MinMax(item.p3);
            } while (true);
            STLprecision = extent.Size * 1e-6;
            pointsOctTree = new OctTree<insertableGeoPoint>(extent, STLprecision);
            for (int i = 0; i < items.Count; i++)
            {
                triangle t = new triangle(InsertOrFindPoint(items[i].p1), InsertOrFindPoint(items[i].p2), InsertOrFindPoint(items[i].p3));
                triangles.Add(t);
            }
            foreach (triangle triangle in triangles) MakeEdges(triangle);
#if DEBUG
            GeoObjectList dbgl = new GeoObjectList();
            foreach (edge edge in edges.Values)
            {
                Line l = Line.TwoPoints(points[edge.p1], points[edge.p2]);
                dbgl.Add(l);
            }
#endif
            foreach (edge edge in edges.Values) CalcBendingAngle(edge);
            // Find all planar faces, consisting of multiple trianglestriangles
            foreach (triangle triangle in triangles)
            {
                if (triangle.surface == null && triangle.e1.bendingAngle < 1e-6 && triangle.e2.bendingAngle < 1e-6 && triangle.e3.bendingAngle < 1e-6)
                {   // all edges have planar connecting triangles. Maybe there are planar faces, with no such triangle
                    CollectPlanarTriangles(triangle);
                }
            }


            /*            OctTree<Vertex> verticesOctTree = null;
                        STLItem tr;
                        int cnt = 0;
                        Set<Face> allFaces = new Set<Face>();
                        GeoObjectList dbgl = new GeoObjectList();
                        do
                        {
                            tr = GetNextSTLItem();
                            if (tr == null) break;
                            if (verticesOctTree == null) verticesOctTree = new OctTree<Vertex>(new BoundingCube(tr.p1, tr.p2, tr.p3), 1e-6);
                            try
                            {
                                PlaneSurface ps = new PlaneSurface(tr.p1, tr.p2, tr.p3);
                                Vertex v1 = VertexFromPoint(verticesOctTree, tr.p1);
                                Vertex v2 = VertexFromPoint(verticesOctTree, tr.p2);
                                Vertex v3 = VertexFromPoint(verticesOctTree, tr.p3);
                                Edge e1 = Vertex.SingleConnectingEdge(v1, v2);
                                if (e1 != null && e1.SecondaryFace != null)
                                { }
                                if (e1 == null || e1.SecondaryFace != null) e1 = new Edge(Line.TwoPoints(v1.Position, v2.Position), v1, v2);
                                Edge e2 = Vertex.SingleConnectingEdge(v2, v3);
                                if (e2 != null && e2.SecondaryFace != null)
                                { }
                                if (e2 == null || e2.SecondaryFace != null) e2 = new Edge(Line.TwoPoints(v2.Position, v3.Position), v2, v3);
                                Edge e3 = Vertex.SingleConnectingEdge(v3, v1);
                                if (e3 != null && e3.SecondaryFace != null)
                                { }
                                if (e3 == null || e3.SecondaryFace != null) e3 = new Edge(Line.TwoPoints(v3.Position, v1.Position), v3, v1);
                                dbgl.Add(Line.TwoPoints(v1.Position, v2.Position));
                                dbgl.Add(Line.TwoPoints(v2.Position, v3.Position));
                                dbgl.Add(Line.TwoPoints(v3.Position, v1.Position));
                                Face fc = Face.Construct();
                                fc.Surface = ps;
                                //Line2D l1 = new Line2D(ps.Plane.Project(tr.p1), ps.Plane.Project(tr.p2));
                                //Line2D l2 = new Line2D(ps.Plane.Project(tr.p2), ps.Plane.Project(tr.p3));
                                //Line2D l3 = new Line2D(ps.Plane.Project(tr.p3), ps.Plane.Project(tr.p1));
                                //if (e1.PrimaryFace == null) e1.SetPrimary(fc, l1, true);
                                //else e1.SetSecondary(fc, l1, false);
                                //if (e2.PrimaryFace == null) e2.SetPrimary(fc, l2, true);
                                //else e2.SetSecondary(fc, l2, false);
                                //if (e3.PrimaryFace == null) e3.SetPrimary(fc, l3, true);
                                //else e3.SetSecondary(fc, l3, false);
                                e1.SetFace(fc, e1.Vertex1 == v1);
                                e2.SetFace(fc, e2.Vertex1 == v2);
                                e3.SetFace(fc, e3.Vertex1 == v3);
                                fc.Set(ps, new Edge[][] { new Edge[] { e1, e2, e3 } });
                                allFaces.Add(fc);
                                ++cnt;
                            }
                            catch (ModOpException)
                            {
                                // empty triangle, plane construction failed
                            }
                        } while (tr != null);
                        while (!allFaces.IsEmpty())
                        {
                            Shell part = Shell.CollectConnected(allFaces);
            #if DEBUG
                            // TODO: some mechanism to tell whether and how to reverse engineer the stl file
                            double precision;
                            if (numnum == 0) precision = part.GetExtent(0.0).Size * 1e-5;
                            else precision = Math.Pow(10, -numdec / (double)(numnum)); // numdec/numnum is average number of decimal places
                            part.ReconstructSurfaces(precision);
            #endif
                            res.Add(part);
                        }
                        return res.ToArray();
            */
            return null;
        }

        private void CollectPlanarTriangles(triangle triangle)
        {
            HashSet<triangle> planarTriangles = new HashSet<triangle>();
            void AddPlanar(triangle t)
            {
                foreach (edge e in new edge[] { t.e1, t.e2, t.e3 })
                {
                    if (e != null && e.bendingAngle < 1e-6)
                    {
                        triangle o = e.OtherTraingle(t);
                        if (o != null && !planarTriangles.Contains(o))
                        {
                            planarTriangles.Add(o);
                            AddPlanar(o);
                        }
                    }
                }
            }
            planarTriangles.Add(triangle);
            AddPlanar(triangle);
            HashSet<int> pointIndices = new HashSet<int>();
            foreach (triangle t in planarTriangles)
            {
                pointIndices.Add(t.p1);
                pointIndices.Add(t.p2);
                pointIndices.Add(t.p3);
            }
            List<GeoPoint> trianglePoints = new List<GeoPoint>();
            foreach (int i in pointIndices) trianglePoints.Add(points[i]);
            double error = GaussNewtonMinimizer.PlaneFit(new ListToIArray<GeoPoint>(trianglePoints), 1e-6, out Plane plane);
            PlaneSurface ps = new PlaneSurface(plane);
            foreach (triangle t in planarTriangles) t.surface = ps;
        }

        public Shell[] Read(byte[] byteArray)
        {
            bool isASCII = false;

            using (sr)
            {
                char[] head = new char[5];
                int read = sr.ReadBlock(head, 0, 5);
                if (read != 5) throw new ApplicationException("cannot read from file");
                if (new string(head) == "solid") isASCII = true;
                else isASCII = false;
            }
            if (isASCII)
            {
                sr = new StreamReader(new MemoryStream(byteArray));
                string title = sr.ReadLine();
            }
            else
            {
                br = new BinaryReader(new MemoryStream(byteArray));
                br.ReadBytes(80);
                uint nrtr = br.ReadUInt32();
            }
            // use the above code !
            return res.ToArray();
        }

        private Vertex VertexFromPoint(OctTree<Vertex> verticesOctTree, GeoPoint closeTo)
        {
            Vertex[] close = verticesOctTree.GetObjectsFromPoint(closeTo);
            for (int i = 0; i < close.Length; i++)
            {
                if ((close[i].Position | closeTo) == 0.0)
                {
                    return close[i];
                }
            }
            Vertex res = new Vertex(closeTo);
            verticesOctTree.AddObject(res);
            return res;
        }

        private abstract class TriangleReader
        {
            private int _numdec = 0, _numnum = 0;


            public int numdec
            {
                get { return _numdec; }
            }

            public int numnum
            {
                get { return _numnum; }
            }

        private void accumulatePrecision(params string[] number)
        {
            for (int i = 0; i < number.Length; i++)
            {
                if (number[i].IndexOf('.') > 0)
                {
                    ++numnum;
                    numdec += number[i].Length - number[i].IndexOf('.') - 1;
                }
            }
        }
        private STLItem GetNextSTLItem()
        {
            if (isASCII)
            {
                triangle res = null;

                if (ReadVector(out GeoVector normal) &&
                    CheckLine("outer loop") &&
                    ReadPoint(out GeoPoint p1) &&
                    ReadPoint(out GeoPoint p2) &&
                    ReadPoint(out GeoPoint p3) &&
                    CheckLine("endloop") &&
                    CheckLine("endfacet"))
                {
                    res = new triangle(p1, p2, p3, normal);
                }

                return res;
            }

            private bool ReadLine(out string line)
            {
                bool ok = false;
                line = null;

                try
                {
                    string[] facet = sr.ReadLine().Trim().Split(' ');
                    if (facet.Length != 5 || facet[0] != "facet" || facet[1] != "normal") return null;
                    if (!double.TryParse(facet[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double nx)) return null;
                    if (!double.TryParse(facet[3], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double ny)) return null;
                    if (!double.TryParse(facet[4], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double nz)) return null;
                    accumulatePrecision(facet[2], facet[3], facet[4]);
                    if (sr.ReadLine().Trim() != "outer loop") return null;
                    string[] vertex = sr.ReadLine().Trim().Split(' ');
                    if (vertex.Length != 4 || vertex[0] != "vertex") return null;
                    if (!double.TryParse(vertex[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p1x)) return null;
                    if (!double.TryParse(vertex[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p1y)) return null;
                    if (!double.TryParse(vertex[3], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p1z)) return null;
                    accumulatePrecision(vertex[1], vertex[2], vertex[3]);
                    vertex = sr.ReadLine().Trim().Split(' ');
                    if (vertex.Length != 4 || vertex[0] != "vertex") return null;
                    if (!double.TryParse(vertex[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p2x)) return null;
                    if (!double.TryParse(vertex[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p2y)) return null;
                    if (!double.TryParse(vertex[3], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p2z)) return null;
                    accumulatePrecision(vertex[1], vertex[2], vertex[3]);
                    vertex = sr.ReadLine().Trim().Split(' ');
                    if (vertex.Length != 4 || vertex[0] != "vertex") return null;
                    if (!double.TryParse(vertex[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p3x)) return null;
                    if (!double.TryParse(vertex[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p3y)) return null;
                    if (!double.TryParse(vertex[3], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double p3z)) return null;
                    accumulatePrecision(vertex[1], vertex[2], vertex[3]);
                    if (sr.ReadLine().Trim() != "endloop") return null;
                    if (sr.ReadLine().Trim() != "endfacet") return null;
                    STLItem res = new STLItem(new GeoPoint(p1x, p1y, p1z), new GeoPoint(p2x, p2y, p2z), new GeoPoint(p3x, p3y, p3z), new GeoVector(nx, ny, nz));
                    return res;
                }
                catch (IOException)
                {
                    return null;
                }
            }

            public override triangle GetNextTriangle()
            {
                if (_br.BaseStream.Position >= _br.BaseStream.Length) return null;
                try
                {
                    GeoVector normal = new GeoVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    GeoPoint p1 = new GeoPoint(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    GeoPoint p2 = new GeoPoint(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    GeoPoint p3 = new GeoPoint(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    int attr = br.ReadUInt16();
                    STLItem res = new STLItem(p1, p2, p3, normal);
                    return res;
                }
                catch (EndOfStreamException)
                {
                    return null;
                }
            }

            private GeoPoint ReadPoint()
            {
                return new GeoPoint(_br.ReadSingle(), _br.ReadSingle(), _br.ReadSingle());
            }

            private GeoVector ReadVector()
            {
                return new GeoVector(_br.ReadSingle(), _br.ReadSingle(), _br.ReadSingle());
            }

            public override void Close()
            {
                if (_br != null)
                    _br.Close();
            }
        }
    }
}
