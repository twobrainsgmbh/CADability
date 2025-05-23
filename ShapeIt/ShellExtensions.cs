using CADability.GeoObject;
using CADability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using CADability.Curve2D;
using CADability.Shapes;
using Wintellect.PowerCollections;

namespace ShapeIt
{
    internal static class ShellExtensions
    {
        public static int GetFaceDistances(this Shell shell, Face distanceFrom, GeoPoint touchingPoint, out List<Face> distanceTo, out List<double> distance, out List<GeoPoint> pointsFrom, out List<GeoPoint> pointsTo)
        {
            distanceTo = new List<Face>();
            distance = new List<double>();
            pointsFrom = new List<GeoPoint>();
            pointsTo = new List<GeoPoint>();
            foreach (Face face in shell.Faces)
            {
                if (face == distanceFrom) continue;
                if (Surfaces.ParallelDistance(distanceFrom.Surface, distanceFrom.Domain, face.Surface, face.Domain, touchingPoint, out GeoPoint2D uv1, out GeoPoint2D uv2))
                {
                    GeoPoint pFrom = distanceFrom.Surface.PointAt(uv1);
                    GeoPoint pTo = face.Surface.PointAt(uv2);
                    double dist = pFrom | pTo;
                    if (dist > Precision.eps)
                    {
                        distanceTo.Add(face);
                        distance.Add(dist);
                        pointsFrom.Add(pFrom);
                        pointsTo.Add(pTo);
                    }
                }
            }
            return distanceTo.Count;
        }
        public enum AdjacencyType
        {
            Unknown,
            Open,
            SameSurface,
            Tangent,
            Convex,
            Concave,
            Mixed
        }
        public static AdjacencyType Adjacency(this Edge edge)
        {
            if (edge.SecondaryFace != null)
            {
                if (edge.PrimaryFace.Surface.SameGeometry(edge.PrimaryFace.Domain, edge.SecondaryFace.Surface, edge.SecondaryFace.Domain, Precision.eps, out ModOp2D _)) return AdjacencyType.SameSurface;
                if (edge.IsTangentialEdge()) return AdjacencyType.Tangent;
                // there should be a test for "mixed"
                Vertex v1 = edge.StartVertex(edge.PrimaryFace);
                GeoPoint2D uvp = v1.GetPositionOnFace(edge.PrimaryFace);
                GeoPoint2D uvs = v1.GetPositionOnFace(edge.SecondaryFace);
                GeoVector curveDir;
                if (edge.Forward(edge.PrimaryFace)) curveDir = edge.Curve3D.StartDirection;
                else curveDir = -edge.Curve3D.EndDirection;
                double orientation = curveDir * (edge.PrimaryFace.Surface.GetNormal(uvp) ^ edge.SecondaryFace.Surface.GetNormal(uvs));
                if (orientation > 0) return AdjacencyType.Convex;
                else return AdjacencyType.Concave;
            }
            else
            {
                return AdjacencyType.Open;
            }
        }
        public static bool AllEdgesAreConvex(this Face face, bool reverse)
        {
            AdjacencyType toFollow = reverse ? AdjacencyType.Concave : AdjacencyType.Convex;
            foreach (Edge edge in face.AllEdges)
            {
                if (edge.Adjacency() != AdjacencyType.Tangent && edge.Adjacency() != toFollow) return false;
            }
            return true;
        }
        public static List<List<Face>> GetConvexParts(this Shell shell, bool reverse)
        {
            HashSet<Face> faces = new HashSet<Face>(shell.Faces);
            var visited = new HashSet<Face>();
            var result = new List<List<Face>>();

            AdjacencyType toFollow = reverse ? AdjacencyType.Concave : AdjacencyType.Convex;

            foreach (var face in faces)
            {
                if (visited.Contains(face))
                    continue;

                // start a new region
                var region = new List<Face>();
                var queue = new Queue<Face>();
                queue.Enqueue(face);
                visited.Add(face);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    region.Add(current);

                    foreach (var edge in current.Edges)
                    {
                        // only respect convex or tangential connections
                        if (edge.Adjacency() != toFollow &&
                            edge.Adjacency() != AdjacencyType.Tangent)
                            continue;

                        Face neighbor = edge.OtherFace(current);

                        if (neighbor == null || visited.Contains(neighbor) || !neighbor.AllEdgesAreConvex(reverse))
                            continue;

                        if (faces.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                            visited.Add(neighbor);
                        }
                    }
                }

                result.Add(region);
            }

            return result;
        }
        /// <summary>
        /// Make a fillet like face, which fills the gap of two offset faces at the provided <paramref name="axis"/>
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="radius"></param>
        /// <param name="forward"></param>
        /// <param name="backward"></param>
        /// <returns></returns>
        public static Face MakeOffsetFillet(Edge axis, double radius, Edge forward, Edge backward)
        {
            ISurface surface = null;
            GeoVector toInside;
            if (axis.Curve3D is Line line)
            {
                if (axis.Forward(axis.PrimaryFace))
                {
                    Vertex v = axis.StartVertex(axis.PrimaryFace);
                    GeoPoint2D uvp = v.GetPositionOnFace(axis.PrimaryFace);
                    GeoPoint2D uvs = v.GetPositionOnFace(axis.SecondaryFace);
                    toInside = -(axis.PrimaryFace.Surface.GetNormal(uvp).Normalized + axis.SecondaryFace.Surface.GetNormal(uvs).Normalized); // points away from the edge
                    GeoVector dir = line.StartDirection;
                    GeoVector majorAxis = radius * toInside.Normalized;
                    GeoVector minorAxis = radius * (dir ^ majorAxis).Normalized;
                    surface = new CylindricalSurface(v.Position, majorAxis, minorAxis, dir);
                }
                else
                {
                    Vertex v = axis.EndVertex(axis.PrimaryFace);
                    GeoPoint2D uvp = v.GetPositionOnFace(axis.PrimaryFace);
                    GeoPoint2D uvs = v.GetPositionOnFace(axis.SecondaryFace);
                    toInside = -(axis.PrimaryFace.Surface.GetNormal(uvp).Normalized + axis.SecondaryFace.Surface.GetNormal(uvs).Normalized); // points away from the edge
                    GeoVector dir = -line.EndDirection;
                    GeoVector majorAxis = radius * toInside.Normalized;
                    GeoVector minorAxis = radius * (dir ^ majorAxis).Normalized;
                    surface = new CylindricalSurface(v.Position, majorAxis, minorAxis, dir);
                }
            }
            else if (axis.Curve3D is Ellipse bigCircle)
            {
                if (axis.Forward(axis.PrimaryFace))
                {
                    Vertex v = axis.StartVertex(axis.PrimaryFace);
                    GeoVector dirx = (v.Position - bigCircle.Center).Normalized;
                    GeoVector dirz = (bigCircle.SweepParameter > 0) ? bigCircle.Plane.Normal : -bigCircle.Plane.Normal;
                    GeoVector diry = dirz ^ dirx; // still to test
                    surface = new ToroidalSurface(bigCircle.Center, dirx, diry, dirz, bigCircle.MajorRadius, radius);
                }
                else
                {
                    Vertex v = axis.EndVertex(axis.PrimaryFace);
                    GeoVector dirx = -(v.Position - bigCircle.Center).Normalized;
                    GeoVector dirz = (bigCircle.SweepParameter > 0) ? bigCircle.Plane.Normal : -bigCircle.Plane.Normal;
                    GeoVector diry = dirz ^ dirx; // still to test
                    surface = new ToroidalSurface(bigCircle.Center, dirx, diry, dirz, bigCircle.MajorRadius, radius);
                }
            }
            else
            {
                // make a NURBS surface defined by a certain number of circles
            }
            if (surface != null)
            {
                // we need 4 edges to make this face. Two edges are already provided as parameters, the other two edges are arcs at the start- and endpoint of the axis
                GeoPoint center = axis.Vertex1.Position; // this is the startpoint of axis.Curve3D
                GeoVector toOutside = (axis.PrimaryFace.Surface.GetNormal(axis.Vertex1.GetPositionOnFace(axis.PrimaryFace)).Normalized +
                             axis.SecondaryFace.Surface.GetNormal(axis.Vertex1.GetPositionOnFace(axis.SecondaryFace)).Normalized);
                Plane pln = new Plane(center, -toOutside, toOutside ^ axis.Curve3D.StartDirection);
                Ellipse elli1 = Ellipse.Construct();
                Vertex v1 = Math.Abs(pln.Distance(forward.Vertex1.Position)) < Math.Abs(pln.Distance(forward.Vertex2.Position)) ? forward.Vertex1 : forward.Vertex2;
                Vertex v2 = Math.Abs(pln.Distance(backward.Vertex1.Position)) < Math.Abs(pln.Distance(backward.Vertex2.Position)) ? backward.Vertex1 : backward.Vertex2;
                elli1.SetArc3Points(v1.Position, center + radius * toOutside.Normalized, v2.Position, pln);

                center = axis.Vertex2.Position; // this is the endpoint of axis.Curve3D
                toOutside = (axis.PrimaryFace.Surface.GetNormal(axis.Vertex2.GetPositionOnFace(axis.PrimaryFace)).Normalized +
                             axis.SecondaryFace.Surface.GetNormal(axis.Vertex2.GetPositionOnFace(axis.SecondaryFace)).Normalized);
                pln = new Plane(center, -toOutside, toOutside ^ axis.Curve3D.EndDirection);
                Ellipse elli2 = Ellipse.Construct();
                v1 = forward.Vertex1 == v1 ? forward.Vertex2 : forward.Vertex1;
                v2 = backward.Vertex1 == v2 ? backward.Vertex2 : backward.Vertex1;
                elli2.SetArc3Points(v1.Position, center + radius * toOutside.Normalized, v2.Position, pln);
                ICurve2D[] curve2Ds = new ICurve2D[4];
                curve2Ds[0] = surface.GetProjectedCurve(forward.Curve3D, Precision.eps);
                curve2Ds[2] = surface.GetProjectedCurve(backward.Curve3D, Precision.eps);
                curve2Ds[1] = surface.GetProjectedCurve(elli1, Precision.eps);
                curve2Ds[3] = surface.GetProjectedCurve(elli2, Precision.eps);
                SimpleShape ss = new SimpleShape(Border.FromUnorientedList(curve2Ds, true));
                return Face.MakeFace(surface, ss);
            }
            return null;
        }
        public static Shell[] GetOffsetNew(this Shell shell, double offset)
        {
            List<List<Face>> convexParts = shell.GetConvexParts(false);

            List<Shell> offsetShells = new List<Shell>();
            foreach (List<Face> convexPart in convexParts)
            {
                List<Face> offsetFaces = new List<Face>();
                Dictionary<Edge, List<Edge>> offsetEdges = new Dictionary<Edge, List<Edge>>();
                foreach (Face face in convexPart)
                {
                    Dictionary<Vertex, Vertex> vertexToVertex = new Dictionary<Vertex, Vertex>();
                    Vertex[] vtx = face.Vertices;
                    foreach (Vertex v in vtx)
                    {
                        GeoPoint2D pf = v.GetPositionOnFace(face);
                        GeoVector normal = face.Surface.GetNormal(pf);
                        if (!normal.IsNullVector())
                        {
                            GeoPoint p = v.Position + offset * normal.Normalized;
                            Vertex vn = new Vertex(p);
                            vertexToVertex[v] = vn;
                        }
                    }
                    Face offsetFace = face.GetOffset(offset, vertexToVertex);
                    if (offsetFace == null) continue;
                    offsetFaces.Add(offsetFace);
                    foreach (Edge edge in face.AllEdges)
                    {
                        if (!edge.IsTangentialEdge())
                        {
                            if (vertexToVertex.ContainsKey(edge.Vertex1) && vertexToVertex.ContainsKey(edge.Vertex1))
                            {
                                IEnumerable<Edge> edgeCandidates = vertexToVertex[edge.Vertex1].AllEdges.Intersect(vertexToVertex[edge.Vertex2].AllEdges);
                                // edgeCandidates contains the parallel edge to edge, but in some cases there are two possible edges with the same start- and endvertex
                                Edge found = null;
                                foreach (Edge candidate in edgeCandidates)
                                {
                                    if (found == null) found = candidate;
                                    else
                                    {
                                        if (Math.Abs(edge.Curve3D.DistanceTo(candidate.Curve3D.PointAt(0.5)) - Math.Abs(offset)) < Precision.eps)
                                        {
                                            found = candidate;
                                        }
                                    }
                                }
                                if (found != null)
                                {
                                    if (!offsetEdges.TryGetValue(edge, out List<Edge> parallelEdges)) offsetEdges[edge] = parallelEdges = new List<Edge>();
                                    parallelEdges.Add(found);
                                }
                            }
                        }
                    }
                    foreach (KeyValuePair<Edge, List<Edge>> item in offsetEdges)
                    {
                        if (item.Value.Count == 2)
                        {
                            Face fillet = MakeOffsetFillet(item.Key, offset, item.Value[0], item.Value[1]);
                            if (fillet != null)
                            {
                                offsetFaces.Add(fillet);
                            }
                        }
                    }
                }
                offsetShells.Add(Shell.FromFaces(offsetFaces.ToArray(), true));
            }
            return null;
        }
    }
}
