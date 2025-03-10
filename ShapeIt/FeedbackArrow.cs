using CADability.GeoObject;
using CADability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeIt
{
    internal class FeedbackArrow
    {
        private static int arrowSize = 12; // should be a píxel on screen value

        /// <summary>
        /// Calculate the distance between the two BRep objects (edge, face or vertex) <paramref name="fromHere"/> and <paramref name="toHere"/>. When both objects are vertices,
        /// the result will be a connection between these two vertices if <paramref name="preferredDirection"/> is the null vector. If there is a preferred direction, the result
        /// will be a line parallel to preferred direction. And if <paramref name="preferredPoint"/> is valid, it will go through it.
        /// If the two objects are faces, the result will be a perpendicular connection between these faces or if preferred direction is valid, it will be a connection in this direction
        /// Also the preferred point will be taken into account, if valid. The same is true for two edges.
        /// The <paramref name="degreeOfFreedom"/> vector will be invalid, if it is two vertices directely, if two vertices with preferred direction or two lines, the <paramref name="degreeOfFreedom"/>
        /// will be in the direction of the lines. Whith two planar faces, the <paramref name="degreeOfFreedom"/> will be a null vector, which means that the plane perpendicular to the 
        /// direction of the result is a degree of freedom. <paramref name="dimStartPoint"/> and <paramref name="dimEndPoint"/> may be used for the display of a dimension line. If
        /// provided by this method. E.g. the closest position of an arc
        /// </summary>
        /// <param name="fromHere"></param>
        /// <param name="toHere"></param>
        /// <param name="preferredDirection"></param>
        /// <param name="preferredPoint"></param>
        /// <param name="degreeOfFreedom"></param>
        /// <param name="dimStartPoint">actual point, which was used for the calculation</param>
        /// <param name="dimEndPoint">actual point, which was used for the calculation</param>
        /// <returns></returns>
        public static (GeoPoint startPoint, GeoPoint endPoint) DistanceBetweenObjects(object fromHere, object toHere, GeoVector preferredDirection, GeoPoint preferredPoint,
            out GeoVector degreeOfFreedom, out GeoPoint dimStartPoint, out GeoPoint dimEndPoint)
        {
            degreeOfFreedom = GeoVector.Invalid;
            dimStartPoint = dimEndPoint = GeoPoint.Invalid;

            bool swapped = false;
            GeoPoint startPoint = GeoPoint.Invalid;
            GeoPoint endPoint = GeoPoint.Invalid;

            // order according to Edge->Face->Vertex to simplify the following cases (C# 8 or greater not available)
            if (string.Compare(fromHere.GetType().Name, toHere.GetType().Name) > 0)
            {
                var temp = fromHere;
                fromHere = toHere;
                toHere = temp;
                swapped = true;
            }

            if (fromHere is Vertex v1 && toHere is Vertex v2)
            {
                if (preferredDirection.IsValid() && !preferredDirection.IsNullVector())
                {   // we need to use this direction, but I don't think we should consider preferredPoint
                    Plane pln = new Plane(v1.Position, preferredDirection); // the plane, perpendicular to the preferred direction
                    GeoPoint v2p = pln.ToLocal(v2.Position);
                    double dist = pln.Distance(v2.Position);
                    startPoint = pln.ToGlobal(new GeoPoint(v2p.x / 2.0, v2p.y / 2.0, 0.0));
                    endPoint = startPoint + dist * preferredDirection.Normalized;
                    degreeOfFreedom = pln.ToGlobal(new GeoVector(v2p.x, v2p.y, 0.0)); // this is the connection of the two points in the plane
                    if (degreeOfFreedom.IsNullVector()) degreeOfFreedom = GeoVector.Invalid;
                }
                else
                {   // simply the connection of both vertices
                    startPoint = v1.Position;
                    endPoint = v2.Position;
                }
            }
            else if (fromHere is Edge e1 && toHere is Edge e2)
            {
                if (preferredDirection.IsValid() && !preferredDirection.IsNullVector())
                {   // we need to use this direction
                    // check for extreme positions of the curve in this direction. E.g. an arc typically has a maximum or minimum, which would be useful here
                    // if multiple points ar available, no criterium is know to prefer one of them
                    double[] ep1 = e1.Curve3D.GetExtrema(preferredDirection);
                    double[] ep2 = e2.Curve3D.GetExtrema(preferredDirection);
                    if (ep1 == null || ep1.Length == 0) dimStartPoint = e1.Curve3D.StartPoint; // maybe there is a better solution
                    else dimStartPoint = e1.Curve3D.PointAt(ep1[0]);
                    if (ep2 == null || ep2.Length == 0) dimEndPoint = e2.Curve3D.EndPoint; // maybe there is a better solution
                    else dimStartPoint = e2.Curve3D.PointAt(ep2[0]);
                    GeoPoint m = new GeoPoint(dimStartPoint, dimEndPoint);
                    if (preferredPoint.IsValid) m = preferredPoint;
                    startPoint = Geometry.DropPL(dimStartPoint, m, preferredDirection);
                    endPoint = Geometry.DropPL(dimEndPoint, m, preferredDirection);
                    if (Curves.GetCommonPlane(e1.Curve3D, e2.Curve3D, out Plane commonPlane))
                    {
                        commonPlane.Intersect(new Plane(m, preferredDirection), out GeoPoint _, out degreeOfFreedom);
                    }
                    else degreeOfFreedom = GeoVector.NullVector;
                }
                else
                {
                    if (e1.Curve3D is Line l1 && e2.Curve3D is Line l2)
                    {   // two lines
                        if (Precision.SameDirection(l1.StartDirection, l2.StartDirection, false))
                        {   // two parallel lines define a plane
                            GeoVector dir = Geometry.DropPL(l2.StartPoint, l1.StartPoint, l1.EndPoint) - l2.StartPoint;
                            if (!dir.IsNullVector())
                            {   // otherwise the lines are identical, there is no solution
                                GeoPoint m = new GeoPoint(l1.PointAt(0.5), l2.PointAt(0.5));
                                if (preferredPoint.IsValid) m = preferredPoint;
                                startPoint = Geometry.DropPL(l1.StartPoint, m, dir);
                                endPoint = Geometry.DropPL(l2.StartPoint, m, dir);
                                degreeOfFreedom = l1.StartDirection;
                            }
                        }
                        else
                        {   // two general lines: find the closts connection
                            double dist = Geometry.DistLL(l1.StartPoint, l1.StartDirection, l2.StartPoint, l2.StartDirection, out double par1, out double par2);
                            if (dist > 0)
                            {
                                startPoint = l1.PointAt(par1);
                                endPoint = l2.PointAt(par2);
                                // no degree of freedom
                            }
                        }
                    }
                    else if (Curves.GetCommonPlane(e1.Curve3D, e2.Curve3D, out Plane commonPlane))
                    {   // the edges are in a common plane
                        double pos1 = 0.5, pos2 = 0.5;
                        if (Curves.NewtonMinDist(e1.Curve3D, ref pos1, e2.Curve3D, ref pos2))
                        {
                            startPoint = e1.Curve3D.PointAt(pos1);
                            endPoint = e2.Curve3D.PointAt(pos2);
                        }
                    }
                    else
                    {
                        Line line = e1.Curve3D as Line;
                        ICurve curve = null;
                        if (line == null)
                        {
                            line = e2.Curve3D as Line;
                            curve = e1.Curve3D;
                        }
                        else
                        {
                            curve = e2.Curve3D;
                        }
                        if (line != null)
                        {   // one of the curves is a line, the other doesnt share a plane with the line

                        }

                    }
                }
            }
            else if (fromHere is Face f1 && toHere is Face f2)
            {
                if (f1.Surface is PlaneSurface ps1 && f2.Surface is PlaneSurface ps2)
                {
                    if (Precision.SameDirection(ps1.Normal, ps2.Normal, false))
                    {   // two parallel planes
                        GeoPoint m;
                        if (preferredPoint.IsValid) m = preferredPoint;
                        else m = new GeoPoint(ps1.Location, ps2.Location);
                        startPoint = ps1.PointAt(ps1.GetLineIntersection(m, ps1.Normal)[0]); // there must be a intersection
                        endPoint = ps2.PointAt(ps2.GetLineIntersection(m, ps2.Normal)[0]); // there must be a intersection
                    }
                    // non parallel surfaces don't work. How would the distance be defined?
                }
                // other combinations with ISurface.GetExtremePositions, but there is some work to do....
            }
            else if (fromHere is Edge e && toHere is Vertex v)
            {

            }
            else if (fromHere is Face f && toHere is Vertex vv)
            {

            }
            else if (fromHere is Edge ee && toHere is Face ff)
            {

            }
            if (swapped) return (endPoint, startPoint);
            else return (startPoint, endPoint);
        }
        /// <summary>
        /// Create an arrow, which visualizes the change of a distance on a shell. The arrow should be visible, i.e. not covered by other parts of this shell.
        /// It must connect the two objects <paramref name="fromHere"/> and <paramref name="toHere"/> and should be parallel to the face <paramref name="onThisFace"/>.
        /// Maybe in future it should also contain the length as a number as a string, which shows the distance.
        /// </summary>
        /// <param name="onThisShell"></param>
        /// <param name="fromHere"></param>
        /// <param name="toHere"></param>
        /// <param name="onThisFace"></param>
        /// <param name="pickPoint"></param>
        /// <param name="projection"></param>
        /// <returns></returns>
        public static GeoObjectList MakeLengthArrow(Shell onThisShell, object fromHere, object toHere, Face onThisFace, GeoVector forceDirection, GeoPoint pickPoint, Projection projection)
        {
            GeoObjectList res = new GeoObjectList();
            (GeoPoint startPoint, GeoPoint endPoint) d = DistanceBetweenObjects(fromHere, toHere, forceDirection, pickPoint, out GeoVector freedom, out GeoPoint dimStartPoint, out GeoPoint dimEndPoint);
            if (d.startPoint.IsValid)
            {   // find a position, where the arrow would be visible
                GeoVector dir = d.endPoint - d.startPoint;
                Plane pln;
                GeoVector xdir = projection.Direction ^ (d.endPoint - d.startPoint);
                if (xdir.IsNullVector()) pln = new Plane(d.startPoint, projection.Direction);
                else pln = new Plane(d.startPoint, xdir, (d.endPoint - d.startPoint));
                // now we have two points, which tell the points, where the distance is meassured, at the point, where the user clicked.
                // if fromHere and toHere are faces, we would like to put arrows from the outside onto the face. startPoint to endPoint specify the direction.
                // The arrows are simple triangles rotated to show maximum widt in the direction of the viewer
                if (fromHere is Face ff)
                {
                    GeoVector n = ff.Surface.GetNormal(ff.Surface.PositionOf(d.startPoint));
                    Face triangle;
                    if (n * dir < 0) triangle = FeedbackArrow.MakeSimpleTriangle(d.startPoint, -dir, projection);
                    else triangle = FeedbackArrow.MakeSimpleTriangle(d.startPoint, dir, projection);
                    res.Add(triangle);
                }
                if (toHere is Face ft)
                {
                    GeoVector n = ft.Surface.GetNormal(ft.Surface.PositionOf(d.startPoint));
                    Face triangle;
                    if (n * dir < 0) triangle = FeedbackArrow.MakeSimpleTriangle(d.endPoint, -dir, projection);
                    else triangle = FeedbackArrow.MakeSimpleTriangle(d.endPoint, dir, projection);
                    res.Add(triangle);
                }
                // now find a place where to display the dimension line
                Plane testplane = new Plane(new GeoPoint(d.startPoint, d.endPoint), dir); // a plane perpendicular to the connection of the two points, which represent the distance
                if (onThisShell != null)
                {
                    double arrowLength = projection.DeviceToWorldFactor * arrowSize;
                    GeoVector2D[] directions = new GeoVector2D[8];
                    for (int i = 0; i < 8; i++) directions[i] = Angle.Deg(i * 45).Direction;
                    for (int i = 0; i < 8; i++)
                    {
                        GeoVector lineDir = testplane.ToGlobal(Angle.Deg(i * 45).Direction);
                        GeoPoint poutside = onThisShell.GetLineIntersection(testplane.Location, lineDir).MinByWithDefault(testplane.Location, p => -Geometry.LinePar(testplane.Location, lineDir, p));
                        // with an empty list of intersection we will get testplane.Location
                        Line l1 = Line.MakeLine(d.startPoint, poutside - 0.5 * dir + arrowLength * 5 * lineDir);
                        Line l2 = Line.MakeLine(d.endPoint, poutside + 0.5 * dir + arrowLength * 5 * lineDir);
                        res.Add(l1);
                        res.Add(l2);
                        Line l3 = Line.MakeLine(l1.EndPoint - arrowLength * lineDir, l2.EndPoint - arrowLength * lineDir);
                        res.Add(l3);
                        res.Add(MakeSimpleTriangle(l3.EndPoint, -l3.EndDirection, l1.StartDirection, projection));
                        res.Add(MakeSimpleTriangle(l3.StartPoint, l3.StartDirection, l1.StartDirection, projection));
                    }
                }
                // res = projection.MakeArrow(d.startPoint, d.endPoint, pln, Projection.ArrowMode.twoArrows);
            }
            return res;
        }
        private static Face MakeSimpleTriangle(GeoPoint point, GeoVector dir, Projection projection)
        {
            GeoVector hor = projection.Direction ^ dir;
            if (hor.IsNullVector()) hor = GeoVector.XAxis ^ dir;
            if (hor.IsNullVector()) hor = GeoVector.YAxis ^ dir;
            hor.Norm();
            double arrowLength = projection.DeviceToWorldFactor * arrowSize;
            GeoPoint p1 = point + arrowLength * dir.Normalized + (arrowLength / 2) * hor;
            GeoPoint p2 = point + arrowLength * dir.Normalized - (arrowLength / 2) * hor;
            return Face.MakeFace(p1, p2, point);
        }
        private static Face MakeSimpleTriangle(GeoPoint point, GeoVector dir, GeoVector perpDir, Projection projection)
        {
            GeoVector hor = perpDir;
            if (hor.IsNullVector()) hor = GeoVector.XAxis ^ dir;
            if (hor.IsNullVector()) hor = GeoVector.YAxis ^ dir;
            hor.Norm();
            double arrowLength = projection.DeviceToWorldFactor * arrowSize;
            GeoPoint p1 = point + arrowLength * dir.Normalized + (arrowLength / 2) * hor;
            GeoPoint p2 = point + arrowLength * dir.Normalized - (arrowLength / 2) * hor;
            return Face.MakeFace(p1, p2, point);
        }
    }
}
