using CADability.GeoObject;
using CADability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    }
}
