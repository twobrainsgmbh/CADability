using CADability.GeoObject;
using System;
using System.Collections.Generic;
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
        HashSet<Face> forwardMovingFaces = new HashSet<Face>(); // faces to be pushed forward (or staying fixed depending on mode)
        HashSet<Face> backwardMovingFaces = new HashSet<Face>(); // faces to be pushed backward (or staying fixed depending on mode)
        object forwardMeasureBound; // an edge, face or vertex for the distance in forward direction
        object backwardMeasureBound; // an edge, face or vertex for the distance in backward direction

        private LengthInput distanceInput; // the current distance
        private MultipleChoiceInput modeInput; // forward, backward, symmetric
        private GeoObjectInput forwardMeassureInput; // to define the object for meassuring the distance in forward direction
        private GeoObjectInput backwardMeassureInput; // to define the object for meassuring the distance in backward direction
        private StringInput nameInput; // Name for the Parametrics when added to the shell
        private BooleanInput preserveInput; // preserve this value, when other parametrics are applied
        private bool validResult; // inputs are ok and produce a valid result

        private string name = "";
        private double distance;

        public ParametricsExtrudeAction(IEnumerable<Face> faces, IEnumerable<Edge> edges, Plane plane, IFrame frame)
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
            // now forwardMovingFaces contain all the faces connectd to the faces to be streched in the forward direction
            Shell.CombineFaces(forwardMovingFaces, forwardBarrier);
            Shell.CombineFaces(backwardMovingFaces, backwardBarrier);
            // forwardMovingFaces and backwardMovingFaces must be disjunct!
        }

        public override string GetID()
        {
            return "Constr.Parametrics.Extrude";
        }
        public override void OnSetAction()
        {
            base.TitleId = "Constr.Parametrics.Extrude";
            base.ActiveObject = shell.Clone();
            if (shell.Layer != null) shell.Layer.Transparency = 128;
            List<InputObject> actionInputs = new List<InputObject>();

            distanceInput = new LengthInput("Extrude.Distance");
            distanceInput.GetLengthEvent += DistanceInput_GetLengthEvent;
            distanceInput.SetLengthEvent += DistanceInput_SetLengthEvent;
            actionInputs.Add(distanceInput);

            forwardMeassureInput = new GeoObjectInput("Extrude.MeassureForwardObject");
            forwardMeassureInput.Optional = true;
            forwardMeassureInput.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverForwardObject);
            actionInputs.Add(forwardMeassureInput);

            backwardMeassureInput = new GeoObjectInput("Extrude.MeassureBackwardObject");
            backwardMeassureInput.Optional = true;
            backwardMeassureInput.MouseOverGeoObjectsEvent += new GeoObjectInput.MouseOverGeoObjectsDelegate(OnMouseOverBackwardObject);
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

        }

        private string NameInput_GetStringEvent()
        {
            return name;
        }

        private void NameInput_SetStringEvent(string val)
        {
            name = val;
        }

        private void ModeInput_SetChoiceEvent(int val)
        {
        }

        private bool OnMouseOverBackwardObject(GeoObjectInput sender, IGeoObject[] TheGeoObjects, bool up)
        {
            return false;
        }

        private bool OnMouseOverForwardObject(GeoObjectInput sender, IGeoObject[] TheGeoObjects, bool up)
        {
            return false;
        }

        private bool DistanceInput_SetLengthEvent(double Length)
        {
            distance = Length;
            return false;
        }

        private double DistanceInput_GetLengthEvent()
        {
            return distance;
        }
    }
}
