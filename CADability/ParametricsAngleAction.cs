using CADability.Actions;
using CADability.GeoObject;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CADability.Actions
{
    /// <summary>
    /// Action to modify the angle between faces.
    /// The angle between two faces is determined depending on the surfaces:
    /// For two planes, the axis is the intersection line, the angle is the angle between the normals.
    /// For objects with an axis (cylinder cone, torus?) the axis is the cross product of the two directions, or the shortest line between these axis.
    /// When parallel, it is the normal of the common plane somewhere at the extent of the faces.
    /// For axis object and plane this is ambiguous, when the axis is perpendicular to the plane
    /// </summary>
    internal class ParametricsAngleAction : ConstructAction
    {
        List<Face> facesToRotate; // all faces which should be rotated by this parametric
        Face primaryRotationFace; // the face which is beeing rotated and which specifies the angle
        Face referenceFace; // the face to which the angle is meassured
        CoordSys axis; // the rotation axis: z-direction is the rotation axis, x-direction is the angle reference

        GeoObjectInput toRotate;
        GeoObjectInput reference;
        AngleInput rotationAngle;

        public ParametricsAngleAction(List<Face> facesToRotate, Face referenceFace)
        {
            this.facesToRotate = facesToRotate;
            primaryRotationFace = facesToRotate[0]; // the first face is the face, which specifies the angle
            this.facesToRotate.RemoveAt(0);
            this.referenceFace = referenceFace;
        }

        double calcAxis()
        {
            if (primaryRotationFace.Surface is PlaneSurface pps && referenceFace.Surface is PlaneSurface rps)
            {
                if (pps.Plane.Intersect(rps.Plane, out GeoPoint loc, out GeoVector dir))
                {
                    GeoVector xdir = rps.Normal ^ dir;
                    GeoVector ydir = rps.Normal ^ xdir;
                    axis = new CoordSys(loc, xdir, ydir);
                    return (new Angle(xdir, pps.Normal ^ axis.Normal)).Radian;
                }
            }
            Axis rax = new Axis(GeoPoint.Origin, GeoVector.NullVector); // the "invalid" value
            if (referenceFace.Surface is ICone ic) rax = new Axis(ic.Apex, ic.Axis);
            else if (referenceFace.Surface is ICylinder icy) rax = icy.Axis;
            else if (referenceFace.Surface is PlaneSurface ps) rax = new Axis(ps.Location, ps.Normal);
            Axis pax = new Axis(GeoPoint.Origin, GeoVector.NullVector); // the "invalid" value
            if (primaryRotationFace.Surface is ICone icn) pax = new Axis(icn.Apex, icn.Axis);
            else if (primaryRotationFace.Surface is ICylinder icy) pax = icy.Axis;
            else if (primaryRotationFace.Surface is PlaneSurface ps) pax = new Axis(ps.Location, ps.Normal);

            if (!rax.Direction.IsNullVector() && !pax.Direction.IsNullVector())
            {
                GeoVector xdir = pax.Direction ^ rax.Direction;
                GeoVector ydir = pax.Direction ^ xdir;
                Geometry.DistLL(pax.Location, pax.Direction, rax.Location, rax.Direction, out double par1, out double par2);
                axis = new CoordSys(rax.Location, xdir, ydir);
                return (new Angle(xdir, pax.Direction ^ axis.Normal)).Radian;
            }
            return 0.0;
        }
        public override string GetID()
        {
            return "MenuId.Parametrics.Angle";
        }
        public override void OnSetAction()
        {
            base.TitleId = "Constr.Parametrics.Angle";

            List<InputObject> actionInputs = new List<InputObject>();

            toRotate = new GeoObjectInput("Parametrics.Angle.FacesToRotate");
            toRotate.MultipleInput = true;
            toRotate.FacesOnly = true;
            toRotate.Optional = true;
            toRotate.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverFacesToRotate);
            actionInputs.Add(toRotate);

            GeoObjectInput reference;
            AngleInput rotationAngle;

        }

        private void Refresh()
        {

        }
        private bool OnMouseOverFacesToRotate(GeoObjectInput sender, IGeoObject[] geoObjects, bool up)
        {
            List<Face> faces = geoObjects.OfType<Face>().ToList();
            if (faces.Count > 0)
            {
                if (up)
                {
                    foreach (Face face in faces)
                    {
                        if (facesToRotate.Contains(face)) facesToRotate.Remove(face); // not sure whethwer to remove
                        else facesToRotate.Add(face);
                    }
                    sender.SetGeoObject(facesToRotate.ToArray(), null);
                    Refresh();
                }
                return faces.Count > 0;
            }
            return false;
        }
    }
}
