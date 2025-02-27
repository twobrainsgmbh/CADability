using CADability.Attribute;
using CADability.GeoObject;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace CADability.Actions
{
    internal class ParametricsExtrudeAction : ConstructAction
    {
        public Shell shell; // the shell beeing manipulated by this action
        public HashSet<Face> Faces { get; } // the faces beeing stretched
        public HashSet<Edge> Edges { get; } // the edges in the extrusion direction
        public Plane Plane { get; } // the seperating plane

        private HashSet<Face> forwardMovingFaces = new HashSet<Face>(); // faces to be pushed forward (or staying fixed depending on mode)
        private HashSet<Face> backwardMovingFaces = new HashSet<Face>(); // faces to be pushed backward (or staying fixed depending on mode)
        private object forwardMeasureBound; // an edge, face or vertex for the distance in forward direction
        private object backwardMeasureBound; // an edge, face or vertex for the distance in backward direction
        private GeoPoint startPoint, endPoint; // of the feedback arrow 
        private Plane feedbackPlane; // Plane to display the feedback arrow
        private GeoObjectList offsetFeedBack; // an arrow to indicate the distance
        private ParametricDistanceProperty parametricProperty;

        private LengthInput distanceInput; // the current distance
        private MultipleChoiceInput modeInput; // forward, backward, symmetric
        private BRepObjectInput forwardMeassureInput; // to define the object for meassuring the distance in forward direction
        private BRepObjectInput backwardMeassureInput; // to define the object for meassuring the distance in backward direction
        private StringInput nameInput; // Name for the Parametrics when added to the shell
        private BooleanInput preserveInput; // preserve this value, when other parametrics are applied
        private bool validResult; // inputs are ok and produce a valid result
        private enum Mode { forward, symmetric, backward };
        private Mode mode;

        private string name = "";
        private double distance;
        /// <summary>
        /// Initialize an extrusion action: The shell, which contains the <paramref name="faces"/>, is going to be streched along the normal of the <paramref name="plane"/>.
        /// The distance is meassured from <paramref name="meassureFrom"/> to <paramref name="meassureTo"/>, which may be vertices, edges or faces.
        /// </summary>
        /// <param name="meassureFrom"></param>
        /// <param name="meassureTo"></param>
        /// <param name="faces"></param>
        /// <param name="edges"></param>
        /// <param name="plane"></param>
        /// <param name="frame"></param>
        public ParametricsExtrudeAction(object meassureFrom, object meassureTo, IEnumerable<Face> faces, IEnumerable<Edge> edges, Plane plane, IFrame frame)
        {
            Faces = new HashSet<Face>(faces);
            Edges = new HashSet<Edge>(edges);
            Plane = plane;
            shell = faces.First().Owner as Shell;
            HashSet<Edge> forwardBarrier = new HashSet<Edge>(); // edges which are moved forward and connect a face to be streched with a face to mof
            HashSet<Edge> backwardBarrier = new HashSet<Edge>();
            forwardMovingFaces = new HashSet<Face>(); // faces to be moved forward
            backwardMovingFaces = new HashSet<Face>();
            foreach (Face face in Faces)
            {
                foreach (Edge edge in face.Edges)
                {
                    if (!Edges.Contains(edge))
                    {
                        if (plane.Distance(edge.Vertex1.Position) > 0)
                        {
                            forwardBarrier.Add(edge);
                            if (Faces.Contains(edge.PrimaryFace)) forwardMovingFaces.Add(edge.SecondaryFace);
                            else forwardMovingFaces.Add(edge.PrimaryFace);
                        }
                        else
                        {
                            backwardBarrier.Add(edge);
                            if (Faces.Contains(edge.PrimaryFace)) backwardMovingFaces.Add(edge.SecondaryFace);
                            else backwardMovingFaces.Add(edge.PrimaryFace);
                        }
                    }
                }
            }
            forwardMeasureBound = meassureTo;
            backwardMeasureBound = meassureFrom;
            // now forwardMovingFaces contain all the faces connectd to the faces to be streched in the forward direction
            Shell.CombineFaces(forwardMovingFaces, forwardBarrier);
            Shell.CombineFaces(backwardMovingFaces, backwardBarrier);
            // the faces are the original faces of the shell
            // forwardMovingFaces and backwardMovingFaces must be disjunct!
            bool isDisjunct = !forwardMovingFaces.Intersect(backwardMovingFaces).Any();
            offsetFeedBack = new GeoObjectList();

        }

        public override string GetID()
        {
            return "Constr.Parametrics.Extrude";
        }
        public override void OnSetAction()
        {
            base.TitleId = "Constr.Parametrics.Extrude";
            base.ActiveObject = shell.Clone();
            if (shell.Layer == null)
            {
                if (shell.Owner is Solid sld && sld.Layer != null) { shell.Layer = sld.Layer; }
                else
                {
                    Style st3d = Frame.Project.StyleList.GetDefault(Style.EDefaultFor.Solids);
                    if (st3d != null && st3d.Layer != null) shell.Layer = st3d.Layer;
                    else
                    {
                        shell.Layer = Frame.Project.LayerList.CreateOrFind("3D");
                    }
                }
            }
            if (shell.Layer != null) shell.Layer.Transparency = 20;
            if (offsetFeedBack != null) FeedBack.Add(offsetFeedBack);
            if (forwardMovingFaces != null) FeedBack.Add(forwardMovingFaces);
            if (backwardMovingFaces != null) FeedBack.Add(backwardMovingFaces);

            List<InputObject> actionInputs = new List<InputObject>();

            CalcDistance();

            distanceInput = new LengthInput("Extrude.Distance");
            distanceInput.GetLengthEvent += DistanceInput_GetLengthEvent;
            distanceInput.SetLengthEvent += DistanceInput_SetLengthEvent;
            actionInputs.Add(distanceInput);

            forwardMeassureInput = new BRepObjectInput("Extrude.MeassureForwardObject");
            forwardMeassureInput.Optional = true;
            forwardMeassureInput.MouseOverBRepObjectsEvent += new BRepObjectInput.MouseOverBRepObjectsDelegate(OnMouseOverForwardObject);
            actionInputs.Add(forwardMeassureInput);

            backwardMeassureInput = new BRepObjectInput("Extrude.MeassureBackwardObject");
            backwardMeassureInput.Optional = true;
            backwardMeassureInput.MouseOverBRepObjectsEvent += new BRepObjectInput.MouseOverBRepObjectsDelegate(OnMouseOverBackwardObject);
            actionInputs.Add(backwardMeassureInput);

            modeInput = new MultipleChoiceInput("DistanceTo.Mode", "DistanceTo.Mode.Values", 0);
            modeInput.SetChoiceEvent += ModeInput_SetChoiceEvent;
            actionInputs.Add(modeInput);
            //modeInput.GetChoiceEvent += ModeInput_GetChoiceEvent;

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

            FeedBack.SelectOutline = false;
            validResult = false;
            forwardMeassureInput.SetBRepObject(new object[] { forwardMeasureBound }, forwardMeasureBound);
            backwardMeassureInput.SetBRepObject(new object[] { backwardMeasureBound }, backwardMeasureBound);

        }

        private void CalcDistance()
        {
            if (forwardMeasureBound is Vertex fvtx && backwardMeasureBound is Vertex bvtx)
            {
                double d2 = Plane.Distance(fvtx.Position); // should be >0
                double d1 = Plane.Distance(bvtx.Position); // should be <0
                distance = d2 - d1;
                startPoint = Plane.Location + d1 * Plane.Normal;
                endPoint = Plane.Location + d2 * Plane.Normal;
            }
            // need to implement distance Plane->Curve and Plane->Face
        }

        private string NameInput_GetStringEvent()
        {
            return name;
        }

        private void NameInput_SetStringEvent(string val)
        {
            name = val;
        }

        private int ModeInput_GetChoiceEvent()
        {
            return (int)mode;
        }

        private void ModeInput_SetChoiceEvent(int val)
        {
            mode = (Mode)val;
            Refresh();
        }

        private bool OnMouseOverBackwardObject(BRepObjectInput sender, object[] bRepObjects, bool up)
        {
            return false;
        }

        private bool OnMouseOverForwardObject(BRepObjectInput sender, object[] bRepObjects, bool up)
        {
            return false;
        }

        private bool DistanceInput_SetLengthEvent(double length)
        {
            validResult = false;
            if (forwardMeasureBound != null && backwardMeasureBound != null)
            {
                distance = length;
                bool ok = Refresh();
                if (ok)
                {
                    return true;
                }
                else
                {
                    distanceInput.SetError(StringTable.GetString("Parametrics.InvalidPropertyValue", StringTable.Category.label));
                    return false;
                }
            }
            return false;
        }

        private bool Refresh()
        {
            FeedBack.ClearAll();
            feedbackPlane = new Plane(Plane.Location, Plane.Normal, Frame.ActiveView.Projection.Direction ^ Plane.Normal);
            GeoVector dir = Plane.Normal;
            double originalDistance = endPoint | startPoint;
            GeoPoint sp = startPoint, ep = endPoint;
            switch (mode)
            {
                case Mode.forward:
                    ep = endPoint + (distance - originalDistance) * dir;
                    offsetFeedBack = Frame.ActiveView.Projection.MakeArrow(sp, ep, feedbackPlane, Projection.ArrowMode.circleArrow);
                    break;
                case Mode.backward:
                    sp = startPoint - (distance - originalDistance) * dir;
                    offsetFeedBack = Frame.ActiveView.Projection.MakeArrow(ep, sp, feedbackPlane, Projection.ArrowMode.circleArrow);
                    break;
                case Mode.symmetric:
                    sp = startPoint - 0.5 * (distance - originalDistance) * dir;
                    ep = endPoint + 0.5 * (distance - originalDistance) * dir;
                    GeoPoint mp = new GeoPoint(sp, ep);
                    offsetFeedBack = Frame.ActiveView.Projection.MakeArrow(mp, sp, feedbackPlane, Projection.ArrowMode.circleArrow);
                    offsetFeedBack.AddRange(Frame.ActiveView.Projection.MakeArrow(mp, ep, feedbackPlane, Projection.ArrowMode.circleArrow));
                    break;
            }
            Shell sh = null;
            Dictionary<Face, Face> faceDict = null;
            foreach (bool moveConnected in new[] { false, true })
            {   // first try without moving connected faces, if this yields no result, try with moving connected faced
                Parametric parametric = new Parametric(shell);
                Dictionary<Face, GeoVector> allFacesToMove = new Dictionary<Face, GeoVector>();
                GeoVector offset = (distance - originalDistance) * dir;
                switch (mode)
                {
                    case Mode.forward:
                        foreach (Face face in forwardMovingFaces)
                        {
                            allFacesToMove[face] = offset;
                        }
                        foreach (Face face in backwardMovingFaces)
                        {
                            allFacesToMove[face] = GeoVector.NullVector;
                        }
                        break;
                    case Mode.symmetric:
                        foreach (Face face in forwardMovingFaces)
                        {
                            allFacesToMove[face] = 0.5 * offset;
                        }
                        foreach (Face face in backwardMovingFaces)
                        {
                            allFacesToMove[face] = -0.5 * offset;
                        }
                        break;
                    case Mode.backward:
                        foreach (Face face in backwardMovingFaces)
                        {
                            allFacesToMove[face] = -offset;
                        }
                        foreach (Face face in forwardMovingFaces)
                        {
                            allFacesToMove[face] = GeoVector.NullVector;
                        }
                        break;
                }
                parametric.MoveFaces(allFacesToMove, offset, moveConnected);
                if (parametric.Apply())
                {
                    sh = parametric.Result();
                    if (sh != null)
                    {
                        ParametricDistanceProperty.Mode pmode = 0;
                        if (moveConnected) pmode |= ParametricDistanceProperty.Mode.connected;
                        if (mode == Mode.symmetric) pmode |= ParametricDistanceProperty.Mode.symmetric;
                        // create the ParametricDistanceProperty here, because here we have all the information
                        parametric.GetDictionaries(out faceDict, out Dictionary<Edge, Edge> edgeDict, out Dictionary<Vertex, Vertex> vertexDict);
                        // facesToKeep etc. contains original objects of the shell, affectedObjects contains objects of the sh = pm.Result()
                        // the parametricProperty will be applied to sh, so we need the objects from sh
                        object fwd = null; // we need the forward and backward objects of the parametrics clone
                        if (forwardMeasureBound is Vertex fvtx) fwd = vertexDict[fvtx];
                        else if (forwardMeasureBound is Face ffce) fwd = faceDict[ffce];
                        else if (forwardMeasureBound is Edge fedg) fwd = edgeDict[fedg];
                        object bwd = null;
                        if (backwardMeasureBound is Vertex bvtx) bwd = vertexDict[bvtx];
                        else if (backwardMeasureBound is Face bfce) bwd = faceDict[bfce];
                        else if (backwardMeasureBound is Edge bedg) bwd = edgeDict[bedg];
                        if (mode == Mode.backward)
                        {
                            parametricProperty = new ParametricDistanceProperty("", Extensions.LookUp(forwardMovingFaces, faceDict),
                                Extensions.LookUp(backwardMovingFaces, faceDict),
                                parametric.GetAffectedObjects(), pmode, bwd, fwd);
                            parametricProperty.ExtrusionDirection = Plane.Normal;
                        }
                        else
                        {
                            parametricProperty = new ParametricDistanceProperty("", Extensions.LookUp(backwardMovingFaces, faceDict),
                                Extensions.LookUp(forwardMovingFaces, faceDict),
                                parametric.GetAffectedObjects(), pmode, fwd, bwd);
                            parametricProperty.ExtrusionDirection = Plane.Normal;
                        }
                        break;
                    }
                }
            }
            if (sh != null)
            {
                ActiveObject = sh;
                validResult = true;
            }
            else
            {
                faceDict = new Dictionary<Face, Face>();
                ActiveObject = shell.Clone(new Dictionary<Edge, Edge>(), new Dictionary<Vertex, Vertex>(), faceDict);
            }
            IEnumerable<Face> fwclones = Extensions.LookUp(forwardMovingFaces, faceDict);
            IEnumerable<Face> bwclones = Extensions.LookUp(backwardMovingFaces, faceDict);
            ColorDef fwColor = new ColorDef("fwColor", Color.Green);
            ColorDef bwColor = new ColorDef("bwColor", Color.Red);
            foreach (Face fw in fwclones)
            {
                fw.ColorDef = fwColor;
            }
            foreach (Face bw in bwclones)
            {
                bw.ColorDef = bwColor;
            }
            offsetFeedBack.AddRange(fwclones);
            offsetFeedBack.AddRange(bwclones);
            FeedBack.Add(offsetFeedBack);
            return sh != null;
        }

        private double DistanceInput_GetLengthEvent()
        {
            return distance;
        }
        public override void OnDone()
        {
            if (validResult && ActiveObject != null)
            {
                string parametricsName = nameInput.Content;
                Solid sld = shell.Owner as Solid;
                if (sld != null)
                {   // the shell was part of a Solid
                    IGeoObjectOwner owner = sld.Owner; // Model or Block
                    using (Frame.Project.Undo.UndoFrame)
                    {
                        owner.Remove(sld);
                        Solid replacement = Solid.MakeSolid(ActiveObject as Shell);
                        replacement.CopyAttributes(sld);
                        owner.Add(replacement);
                        // here parametricProperty is already consistant with the BRep objects of ActiveObject
                        if (!string.IsNullOrEmpty(parametricsName) && parametricProperty != null)
                        {
                            parametricProperty.Name = parametricsName;
                            parametricProperty.Preserve = preserveInput.Value;
                            replacement.Shells[0].AddParametricProperty(parametricProperty);
                        }
                    }
                }
                else
                {
                    IGeoObjectOwner owner = shell.Owner;
                    using (Frame.Project.Undo.UndoFrame)
                    {
                        owner.Remove(shell);
                        owner.Add(ActiveObject);
                    }
                }
            }
            ActiveObject = null;
            base.OnDone();
        }
        public override void OnRemoveAction()
        {
            if (shell.Layer != null) shell.Layer.Transparency = 0; // make the layer opaque again
            base.OnRemoveAction();
        }
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
        public static GeoObjectList MakeLengthArrow(Shell onThisShell, object fromHere, object toHere, Face onThisFace, GeoPoint pickPoint, Projection projection)
        {
            GeoObjectList res = null;
            (GeoPoint startPoint, GeoPoint endPoint) d = DistanceBetweenObjects(fromHere, toHere, GeoVector.Invalid, GeoPoint.Invalid, out GeoVector freedom, out GeoPoint dimStartPoint, out GeoPoint dimEndPoint);
            if (d.startPoint.IsValid)
            {   // find a position, where the arrow would be visible
                Plane pln = new Plane(d.startPoint, d.endPoint - d.startPoint, GeoVector.ZAxis);
                res = projection.MakeArrow(d.startPoint, d.endPoint, pln, Projection.ArrowMode.twoArrows);
            }
            return res;
        }
    }
}
