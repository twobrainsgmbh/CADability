using CADability;
using CADability.Actions;
using CADability.Attribute;
using CADability.GeoObject;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace ShapeIt
{
    public class ParametricsExtrudeAction : ConstructAction
    {
        public Shell shell; // the shell beeing manipulated by this action
        public HashSet<Face> Faces { get; } // the faces beeing stretched
        public HashSet<Edge> Edges { get; } // the edges in the extrusion direction
        public Plane Plane { get; } // the seperating plane

        private HashSet<Face> forwardMovingFaces = new HashSet<Face>(); // faces to be pushed forward (or staying fixed depending on mode)
        private HashSet<Face> backwardMovingFaces = new HashSet<Face>(); // faces to be pushed backward (or staying fixed depending on mode)
        private Face crossSection; // for display only: where the shell is seperated for the extrusion
        private GeoPoint pickPoint; // for display of arrows, the point, where the shell had been selected
        private IEnumerable<Face> arrows; // the two arrows at the cross section, which remain constant
        private object measureFromHere; // an edge, face or vertex for the distance in forward direction
        private object measureToHere; // an edge, face or vertex for the distance in backward direction
        private GeoPoint startPoint, endPoint; // of the feedback arrow 
        private Plane feedbackPlane; // Plane to display the feedback arrow
        private GeoObjectList feedbackDimension; // an arrow to indicate the distance
        private Feedback feedback;
        private Shell feedbackResult; // the shell, which will be the result
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
        internal ParametricsExtrudeAction(object meassureFrom, object meassureTo, IEnumerable<Face> faces, IEnumerable<Edge> edges, Plane plane,
            Face crossSection, GeoPoint pickPoint, IEnumerable<Face> arrows, IFrame frame)
        {
            Faces = new HashSet<Face>(faces);
            Edges = new HashSet<Edge>(edges);
            Plane = plane;
            this.crossSection = crossSection;
            this.pickPoint = pickPoint;
            this.arrows = arrows;
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
            measureFromHere = meassureTo;
            measureToHere = meassureFrom;
            // now forwardMovingFaces contain all the faces connectd to the faces to be streched in the forward direction
            Shell.CombineFaces(forwardMovingFaces, forwardBarrier);
            Shell.CombineFaces(backwardMovingFaces, backwardBarrier);
            // the faces are the original faces of the shell
            // forwardMovingFaces and backwardMovingFaces must be disjunct!
            bool isDisjunct = !forwardMovingFaces.Intersect(backwardMovingFaces).Any();
            feedbackDimension = new GeoObjectList();
            feedback = new Feedback();
        }

        public override string GetID()
        {
            return "Constr.Parametrics.Extrude";
        }
        public override void OnSetAction()
        {
            base.TitleId = "Constr.Parametrics.Extrude";
            feedbackResult = shell.Clone() as Shell;
            if (crossSection != null) feedback.SelectedObjects.Add(crossSection);

            List<InputObject> actionInputs = new List<InputObject>();

            CalcDistance();

            distanceInput = new LengthInput("Extrude.Distance");
            distanceInput.GetLengthEvent += OnGetDistance;
            distanceInput.SetLengthEvent += OnSetDistance;
            actionInputs.Add(distanceInput);

            backwardMeassureInput = new BRepObjectInput("Extrude.MeassureBackwardObject");
            backwardMeassureInput.Optional = true;
            backwardMeassureInput.MouseOverBRepObjectsEvent += new BRepObjectInput.MouseOverBRepObjectsDelegate(OnMouseOverBackwardObject);
            actionInputs.Add(backwardMeassureInput);

            forwardMeassureInput = new BRepObjectInput("Extrude.MeassureForwardObject");
            forwardMeassureInput.Optional = true;
            forwardMeassureInput.MouseOverBRepObjectsEvent += new BRepObjectInput.MouseOverBRepObjectsDelegate(OnMouseOverForwardObject);
            actionInputs.Add(forwardMeassureInput);

            modeInput = new MultipleChoiceInput("DistanceTo.Mode", "DistanceTo.Mode.Values", 0);
            modeInput.SetChoiceEvent += OnSetDistanceMode;
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

            validResult = false;
            forwardMeassureInput.SetBRepObject(new object[] { measureFromHere }, measureFromHere);
            backwardMeassureInput.SetBRepObject(new object[] { measureToHere }, measureToHere);

            feedback.Attach(CurrentMouseView);
            Refresh();
        }

        private IGeoObject ToGeoObject(object o)
        {
            if (o is IGeoObject go) { return go; }
            if (o is Edge edg) { return edg.Curve3D as IGeoObject; }
            if (o is Vertex vtx)
            {
                CADability.GeoObject.Point pnt = CADability.GeoObject.Point.Construct();
                pnt.Location = vtx.Position;
                pnt.Symbol = PointSymbol.Cross;
                return pnt;
            }
            return null;
        }

        public override void OnViewsChanged()
        {
            feedback.Detach();
            feedback.Attach(CurrentMouseView);
            base.OnViewsChanged();
        }
        private void CalcDistance()
        {
            (startPoint, endPoint) = DistanceCalculator.DistanceBetweenObjects(measureFromHere, measureToHere, Plane.Normal, pickPoint, 
                out GeoVector degreeOfFreedom, out GeoPoint dimStartPoint, out GeoPoint dimEndPoint);
            distance = startPoint | endPoint;
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

        private void OnSetDistanceMode(int val)
        {
            mode = (Mode)val;
            Refresh();
        }

        private bool OnMouseOverBackwardObject(BRepObjectInput sender, object[] bRepObjects, bool up)
        {   // we accept vertex, edge or face, but in the original object, not in the modified object (maybe implement later)
            // the object must be on the correct side of the plane
            object bestObject = null;
            for (int i = 0; i < bRepObjects.Length; i++)
            {
                if (bRepObjects[i] is Vertex vtx && Plane.Distance(vtx.Position) < 0.0) bestObject = vtx;
                else if (bRepObjects[i] is Edge edg && !(bestObject is Vertex))
                {
                    double[] ex = edg.Curve3D.GetExtrema(Plane.Normal);
                    for (int j = 0; j < ex.Length; j++)
                    {
                        if (ex[j] >= 0.0 && ex[j] <= 1.0 && Plane.Distance(edg.Curve3D.PointAt(ex[j])) < 0.0)
                        {
                            bestObject = edg;
                            break;
                        }
                    }
                }
                else if (bRepObjects[i] is Face fce && !(bestObject is Vertex) && !(bestObject is Edge))
                {
                    if (fce.Surface is PlaneSurface ps)
                    {
                        if (Precision.SameDirection(ps.Normal, Plane.Normal, false) && Plane.Distance(fce.AllEdges[0].Vertex1.Position) < 0.0) bestObject = fce;
                    }
                    else
                    {
                        GeoPoint2D[] ex = SurfaceHelper.GetExtrema(fce.Surface, fce.Domain, Plane.Normal);
                        // sender.currentMousePoint
                        // Frame.ActiveView.Projection.Direction
                        // there should be something like Face.GetPickPoint(mousePoint, projection)
                        // with periodic surfaces it is hard to tell on which side
                    }
                }
            }
            if (bestObject != null && up)
            {
                sender.SetBRepObject(new object[] { bestObject }, bestObject);
                measureToHere = bestObject;
                CalcDistance();
                Refresh();
            }
            return bestObject != null;
        }

        private bool OnMouseOverForwardObject(BRepObjectInput sender, object[] bRepObjects, bool up)
        {
            object bestObject = null;
            for (int i = 0; i < bRepObjects.Length; i++)
            {
                if (bRepObjects[i] is Vertex vtx && Plane.Distance(vtx.Position) > 0.0) bestObject = vtx;
                else if (bRepObjects[i] is Edge edg && !(bestObject is Vertex))
                {
                    double[] ex = edg.Curve3D.GetExtrema(Plane.Normal);
                    for (int j = 0; j < ex.Length; j++)
                    {
                        if (ex[j] >= 0.0 && ex[j] <= 1.0 && Plane.Distance(edg.Curve3D.PointAt(ex[j])) > 0.0)
                        {
                            bestObject = edg;
                            break;
                        }
                    }
                }
                else if (bRepObjects[i] is Face fce && !(bestObject is Vertex) && !(bestObject is Edge))
                {
                    if (fce.Surface is PlaneSurface ps)
                    {
                        if (Precision.SameDirection(ps.Normal, Plane.Normal, false) && Plane.Distance(fce.AllEdges[0].Vertex1.Position) > 0.0) bestObject = fce;
                    }
                    else
                    {
                        GeoPoint2D[] ex = SurfaceHelper.GetExtrema(fce.Surface, fce.Domain, Plane.Normal);
                        // with periodic surfaces it is hard to tell on which side
                    }
                }
            }
            if (bestObject != null && up)
            {
                sender.SetBRepObject(new object[] { bestObject }, bestObject);
                measureFromHere = bestObject;
                CalcDistance();
                Refresh();
            }
            return bestObject != null;
        }

        private bool OnSetDistance(double length)
        {
            validResult = false;
            if (measureFromHere != null && measureToHere != null)
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
            feedback.Clear();
            feedbackPlane = new Plane(Plane.Location, Plane.Normal, Frame.ActiveView.Projection.Direction ^ Plane.Normal);
            GeoVector dir = Plane.Normal;
            double originalDistance = endPoint | startPoint;
            GeoPoint sp = startPoint, ep = endPoint;
            switch (mode)
            {
                case Mode.forward:
                    ep = endPoint + (distance - originalDistance) * dir;
                    feedbackDimension = FeedbackArrow.MakeLengthArrow(shell, CloneAndMove(measureFromHere, (distance - originalDistance) * dir), CloneAndMove(measureToHere, GeoVector.NullVector), null, dir, GeoPoint.Invalid, CurrentMouseView);
                    break;
                case Mode.backward:
                    sp = startPoint - (distance - originalDistance) * dir;
                    feedbackDimension = FeedbackArrow.MakeLengthArrow(shell, CloneAndMove(measureFromHere, GeoVector.NullVector), CloneAndMove(measureToHere, -(distance - originalDistance) * dir), null, dir, GeoPoint.Invalid, CurrentMouseView);
                    break;
                case Mode.symmetric:
                    sp = startPoint - 0.5 * (distance - originalDistance) * dir;
                    ep = endPoint + 0.5 * (distance - originalDistance) * dir;
                    GeoPoint mp = new GeoPoint(sp, ep);
                    feedbackDimension = FeedbackArrow.MakeLengthArrow(shell, CloneAndMove(measureFromHere, 0.5 * (distance - originalDistance) * dir),
                        CloneAndMove(measureToHere, -0.5 * (distance - originalDistance) * dir), null, dir, GeoPoint.Invalid, CurrentMouseView);
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
                        if (measureFromHere is Vertex fvtx) fwd = vertexDict[fvtx];
                        else if (measureFromHere is Face ffce) fwd = faceDict[ffce];
                        else if (measureFromHere is Edge fedg) fwd = edgeDict[fedg];
                        object bwd = null;
                        if (measureToHere is Vertex bvtx) bwd = vertexDict[bvtx];
                        else if (measureToHere is Face bfce) bwd = faceDict[bfce];
                        else if (measureToHere is Edge bedg) bwd = edgeDict[bedg];
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
                feedbackResult = sh;
                validResult = true;
            }
            else
            {
                faceDict = new Dictionary<Face, Face>();
                feedbackResult = shell.Clone(new Dictionary<Edge, Edge>(), new Dictionary<Vertex, Vertex>(), faceDict);
            }
            //feedback.FrontFaces.AddRange(forwardMovingFaces);
            //feedback.BackFaces.AddRange(backwardMovingFaces);
            feedback.ShadowFaces.Add(feedbackResult);
            feedback.FrontFaces.Add(ToGeoObject(measureFromHere));
            feedback.BackFaces.Add(ToGeoObject(measureToHere));
            feedback.Arrows.AddRange(feedbackDimension);
            feedback.Arrows.AddRange(arrows);
            if (crossSection != null) feedback.SelectedObjects.Add(crossSection);
            feedback.Refresh();
            return sh != null;
        }

        private object CloneAndMove(object toMove, GeoVector moveBy)
        {
            if (toMove is IGeoObject go)
            {
                IGeoObject res = go.Clone();
                res.Modify(ModOp.Translate(moveBy));
                return res;
            }
            if (toMove is Edge edge)
            {
                IGeoObject res = edge.Curve3D.Clone() as IGeoObject;
                res.Modify(ModOp.Translate(moveBy));
                return res;
            }
            if (toMove is Vertex vtx)
            {
                return new Vertex(ModOp.Translate(moveBy) * vtx.Position);
            }
            return null;
        }

        private double OnGetDistance()
        {
            return distance;
        }
        public override void OnDone()
        {
            if (validResult && feedbackResult != null)
            {
                string parametricsName = nameInput.Content;
                Solid sld = shell.Owner as Solid;
                if (sld != null)
                {   // the shell was part of a Solid
                    IGeoObjectOwner owner = sld.Owner; // Model or Block
                    using (Frame.Project.Undo.UndoFrame)
                    {
                        owner.Remove(sld);
                        Solid replacement = Solid.MakeSolid(feedbackResult as Shell);
                        replacement.CopyAttributes(sld);
                        owner.Add(replacement);
                        // here parametricProperty is already consistant with the BRep objects of feedbackResult
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
                        owner.Add(feedbackResult);
                    }
                }
            }
            feedbackResult = null;
            base.OnDone();
        }
        public override void OnRemoveAction()
        {
            feedback.Detach();
            base.OnRemoveAction();
        }
    }
}
