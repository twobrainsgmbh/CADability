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
        Shell shell;
        List<Face> facesToRotate; // all faces which should be rotated by this parametric
        Face primaryRotationFace; // the face which is beeing rotated and which specifies the angle
        Face referenceFace; // the face to which the angle is meassured
        Axis rotationAxis; // the axis of rotation. The location is close to the mouse position of the click point
        GeoVector fromHere; // Reference vector for the start of the rotation, perpendicular to the rotationAxis
        GeoVector toHereStartValue; // start value of the above
        GeoVector toHere; // the vector to be modified, perpendicular to the rotationAxis
        string parametricsName; // the name for the parametrics in the shall, if provided

        GeoObjectInput facetoRotate;
        GeoObjectInput moreFacestoRotate;
        GeoObjectInput reference;
        AngleInput rotationAngle;
        StringInput nameInput;
        BooleanInput preserveInput;

        public ParametricsAngleAction(Face faceToRotate, Face referenceFace, Axis axis, GeoVector fromHere, GeoVector toHere, IFrame frame)
        {
            Shell shell = faceToRotate.Owner as Shell;
            if (shell == null) throw new ApplicationException("faceToRotate must be part of a shell");
            this.facesToRotate = new List<Face>();
            primaryRotationFace = faceToRotate; // the first face is the face, which specifies the angle
            this.referenceFace = referenceFace;
            this.rotationAxis = axis;
            this.fromHere = fromHere;
            toHereStartValue = this.toHere = toHere;
        }
        /// <summary>
        /// Calculates a rotation axis between the face <paramref name="toRotate"/> and the <paramref name="referenceFace"/>. The returned  axis will be 
        /// close to the <paramref name="pickBeam"/>. The returned vectors fromHere and toHere are the current position of the enclosing angle.
        /// </summary>
        /// <param name="toRotate"></param>
        /// <param name="referenceFace"></param>
        /// <param name="pickBeam"></param>
        /// <returns></returns>
        static public (Axis axis, GeoVector fromHere, GeoVector toHere) GetRotationAxis(Face toRotate, Face referenceFace, Axis pickBeam)
        {
            List<Edge> commonEdges = new List<Edge>(toRotate.AllEdges.Intersect(referenceFace.AllEdges));
            // Implementation for lines:
            double minDist = double.MaxValue;
            Line rotationLine = null;
            GeoPoint rotationPoint = GeoPoint.Origin;
            Edge edge = null;
            for (int i = 0; i<commonEdges.Count; ++i)
            {
                if (commonEdges[i].Curve3D is Line line && !commonEdges[i].IsTangentialEdge())
                {
                    Geometry.ConnectLines(line.StartPoint, line.StartDirection, pickBeam.Location, pickBeam.Direction, out double pos1, out double pos2);
                    GeoPoint p1 = line.StartPoint + pos1 * line.StartDirection;
                    GeoPoint p2 = pickBeam.Location + pos2 * pickBeam.Direction;
                    double d = p1 | p2;
                    if (d<minDist)
                    {
                        minDist = d;
                        rotationLine = line;
                        pos1 = Math.Max(Math.Min(pos1, 1.0), 0.0);
                        rotationPoint = line.StartPoint + pos1 * line.StartDirection; // on the line, which is the edge
                        edge = commonEdges[i];
                    }
                }
            }
            if (rotationLine != null)
            {
                // find the direction from where to go and the direction to where to go
                GeoVector fromHere = referenceFace.Surface.GetNormal(referenceFace.Surface.PositionOf(rotationPoint)) ^ rotationLine.StartDirection;
                if (!edge.Forward(referenceFace)) fromHere = -fromHere;
                GeoVector toHere = toRotate.Surface.GetNormal(toRotate.Surface.PositionOf(rotationPoint)) ^ rotationLine.StartDirection;
                if (!edge.Forward(toRotate)) toHere = -toHere;
                return (new Axis(rotationPoint, rotationLine.StartDirection), fromHere, toHere);
            }

            return (Axis.InvalidAxis, GeoVector.NullVector, GeoVector.NullVector);
        }
        public override string GetID()
        {
            return "MenuId.Parametrics.Angle";
        }
        public override void OnSetAction()
        {
            base.TitleId = "Constr.Parametrics.Angle";

            List<InputObject> actionInputs = new List<InputObject>();

            facetoRotate = new GeoObjectInput("Parametrics.Angle.FaceToRotate");
            facetoRotate.MultipleInput = false;
            facetoRotate.FacesOnly = true;
            facetoRotate.Optional = false;
            facetoRotate.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverFaceToRotate);
            actionInputs.Add(facetoRotate);

            moreFacestoRotate = new GeoObjectInput("Parametrics.Angle.MoreFacesToRotate");
            moreFacestoRotate.MultipleInput = true;
            moreFacestoRotate.FacesOnly = true;
            moreFacestoRotate.Optional = true;
            moreFacestoRotate.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverMoreFacesToRotate);
            actionInputs.Add(moreFacestoRotate);

            reference = new GeoObjectInput("Parametrics.Angle.Reference");
            reference.MultipleInput = false;
            reference.FacesOnly = true;
            reference.Optional = true;
            reference.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverReferenceFace);
            actionInputs.Add(reference);

            rotationAngle = new AngleInput("Parametrics.Angle.Angle");
            rotationAngle.GetAngleEvent += RotationAngle_GetAngleEvent;
            rotationAngle.SetAngleEvent += RotationAngle_SetAngleEvent;
            actionInputs.Add(rotationAngle);

            SeparatorInput separator = new SeparatorInput("Parametrics.AssociateParametric");
            actionInputs.Add(separator);
            nameInput = new StringInput("Parametrics.ParametricsName");
            nameInput.SetStringEvent += NameInput_SetStringEvent;
            nameInput.GetStringEvent += NameInput_GetStringEvent;
            nameInput.Optional = true;
            actionInputs.Add(nameInput);

            preserveInput = new BooleanInput("Parametrics.PreserveValue", "YesNo.Values", false);
            actionInputs.Add(preserveInput);

            SetInput(actionInputs.ToArray());
            base.OnSetAction();

            facetoRotate.SetGeoObject(new IGeoObject[] { primaryRotationFace }, primaryRotationFace);
            facetoRotate.Fixed = primaryRotationFace != null;
            reference.SetGeoObject(new IGeoObject[] { referenceFace }, null);
        }

        private bool RotationAngle_SetAngleEvent(Angle angle)
        {
            return true;
        }

        private Angle RotationAngle_GetAngleEvent()
        {
            return new Angle(fromHere, toHere); // should be between -180° and +180°
        }

        private void Refresh()
        {

        }
        private bool OnMouseOverFaceToRotate(GeoObjectInput sender, IGeoObject[] geoObjects, bool up)
        {
            List<Face> faces = geoObjects.OfType<Face>().ToList();
            if (faces.Count > 0)
            {
                if (up)
                {
                    primaryRotationFace = faces[0];
                    sender.SetGeoObject(new IGeoObject[] { primaryRotationFace }, primaryRotationFace);
                    Refresh();
                }
            }
            return faces.Count > 0;
        }
        private bool OnMouseOverMoreFacesToRotate(GeoObjectInput sender, IGeoObject[] geoObjects, bool up)
        {
            List<Face> faces = geoObjects.OfType<Face>().ToList();
            if (faces.Count > 0)
            {
                if (up)
                {
                    HashSet<Face> current = new HashSet<Face>(sender.GetGeoObjects().OfType<Face>());
                    foreach (Face face in faces)
                    {
                        if (current.Contains(face)) current.Remove(face);
                        else current.Add(face);
                        sender.SetGeoObject(current.ToArray(), null);
                        facesToRotate = new List<Face>(current);
                    }
                    Refresh();
                }
            }
            return faces.Count > 0;
        }
        private bool OnMouseOverReferenceFace(GeoObjectInput sender, IGeoObject[] geoObjects, bool up)
        {
            List<Face> faces = geoObjects.OfType<Face>().ToList();
            if (faces.Count > 0)
            {
                if (up)
                {
                    referenceFace = faces[0];
                    sender.SetGeoObject(new IGeoObject[] { referenceFace }, referenceFace);
                    Refresh();
                }
            }
            return faces.Count > 0;
        }
        private string NameInput_GetStringEvent()
        {
            if (parametricsName == null) return string.Empty;
            else return parametricsName;
        }

        private void NameInput_SetStringEvent(string val)
        {
            parametricsName = val;
        }

    }
}
