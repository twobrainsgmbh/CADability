using CADability;
using CADability.Actions;
using CADability.Attribute;
using CADability.Curve2D;
using CADability.Forms;
using CADability.GeoObject;
using CADability.Shapes;
using CADability.Substitutes;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wintellect.PowerCollections;

namespace ShapeIt
{
    internal class ModellingPropertyEntries : PropertyEntryImpl
    {
        private List<IPropertyEntry> subEntries = new List<IPropertyEntry>(); // the list of property entries in this property page
        private IFrame cadFrame; // the frame (root object of cadability)
        private SelectObjectsAction selectAction; // the select action, which runs parallel to the modelling property page
        private bool isAccumulating; // expect more clicks to add same objects of a certain type (edges, faces)
        private List<IGeoObject> accumulatedObjects = new List<IGeoObject>(); // objects beeing accumulated
        private GeoObjectList currentMenuSelection = new GeoObjectList(); // List of highlighted objects to be displayed, when a entry is selected
        public ModellingPropertyEntries(IFrame cadFrame) : base("Modelling.Properties")
        {
            this.cadFrame = cadFrame;
            selectAction = cadFrame.ActiveAction as SelectObjectsAction;
            if (selectAction != null)
            {
                selectAction.FilterMouseMessagesEvent += FilterSelectMouseMessages;
            }
            cadFrame.ActionTerminatedEvent += ActionTerminated;
            cadFrame.ActionStartedEvent += ActionStarted;
            isAccumulating = false;

        }

        private void ActionStarted(CADability.Actions.Action action)
        {
        }
        private void ActionTerminated(CADability.Actions.Action action)
        {
            if (action.GetID().StartsWith("Modelling"))
            {
                cadFrame.ControlCenter.ShowPropertyPage("Modelling");
            }
        }
        public override PropertyEntryType Flags => PropertyEntryType.Selectable | PropertyEntryType.GroupTitle | PropertyEntryType.HasSubEntries; // PropertyEntryType.ContextMenu | 
        public override IPropertyEntry[] SubItems => subEntries.ToArray();
        private void FilterSelectMouseMessages(SelectObjectsAction.MouseAction mouseAction, CADability.Substitutes.MouseEventArgs e, IView vw, ref bool handled)
        {
            if (mouseAction == SelectObjectsAction.MouseAction.MouseUp && e.Button == CADability.Substitutes.MouseButtons.Left)
            {
                ComposeModellingEntries(e.Location, vw);
            }
        }
        private void ComposeModellingEntries(System.Drawing.Point mousePoint, IView vw)
        {   // a mouse left button up took place. Compose all the entries for objects, which can be handled by 
            // the object(s) under the mouse cursor
            subEntries.Clear(); // build a new list of modelling properties
            GeoObjectList objectsUnderCursor = new GeoObjectList();
            int pickRadius = selectAction.Frame.GetIntSetting("Select.PickRadius", 5);
            Projection.PickArea pa = vw.Projection.GetPickSpace(new Rectangle(mousePoint.X - pickRadius, mousePoint.Y - pickRadius, pickRadius * 2, pickRadius * 2));
            Axis clickBeam = new Axis(pa.FrontCenter, pa.Direction);
            if (isAccumulating && accumulatedObjects.Count > 0)
            {
                IEnumerable<Layer> visiblaLayers = new List<Layer>();
                if (vw is ModelView mv) visiblaLayers = mv.GetVisibleLayers();
                // we are in accumulation mode. 
                objectsUnderCursor = vw.Model.GetObjectsFromRect(pa, new Set<Layer>(visiblaLayers), PickMode.singleFaceAndCurve, null); // returns all the face under the cursor
                for (int i = 0; i < objectsUnderCursor.Count; i++)
                {
                    if (accumulatedObjects[0] is ICurve && objectsUnderCursor[i] is ICurve || accumulatedObjects[0].GetType() == objectsUnderCursor[i].GetType())
                    {   // if this object is already in the accumulated objects, remove it, else add it
                        if (accumulatedObjects.Contains(objectsUnderCursor[i])) accumulatedObjects.Remove(objectsUnderCursor[i]);
                        else accumulatedObjects.Add(objectsUnderCursor[i]);
                    }
                }
            }
            // handle either accumulated objects or the objects under the cursor
            if (accumulatedObjects.Count > 1)
            {   // there must be at least 2 objects of the same type be accumulated to show actions for this group

            }
            else
            {   // show actions for all vertices, edges, faces and curves in 
                // distibute objects into their categories
                List<Face> faces = new List<Face>();
                List<Shell> shells = new List<Shell>(); // only Shells, which are not part of a Solid
                List<Solid> solids = new List<Solid>();
                List<Face> axis = new List<Face>(); // the axis of these faces, which may be cylindrical, conical or toroidal
                List<Edge> edges = new List<Edge>();
                List<ICurve> curves = new List<ICurve>(); // curves, but not axis

                for (int i = 0; i < objectsUnderCursor.Count; i++)
                {
                    if (objectsUnderCursor[i] is Solid sld)
                    {
                        solids.Add(sld);
                    }
                    else if (objectsUnderCursor[i] is Face fc)
                    {
                        faces.Add(fc);
                        if (fc.Owner is Shell sh)
                        {
                            if (sh.Owner is Solid solid) solids.Add(solid);
                            else shells.Add(sh);
                        }
                    }
                    else if (objectsUnderCursor[i] is ICurve crv)
                    {
                        if (objectsUnderCursor[i].Owner is Edge edge) edges.Add(edge);
                        else if (objectsUnderCursor[i].UserData.Contains("CADability.AxisOf"))
                        {
                            Face faceWithAxis = (objectsUnderCursor[i]).UserData.GetData("CADability.AxisOf") as Face;
                            if (faceWithAxis != null) axis.Add(faceWithAxis);
                        }
                        else
                        {
                            curves.Add(crv);
                        }
                    }
                }

                if (edges.Count > 0) // are there features defined by the selected edges?
                {
                    Shell edgesShell = (edges.First().Owner as Face).Owner as Shell;
                    HashSet<Edge> connectedEdges = Shell.ConnectedSameGeometryEdges(edges); // add all faces which have the same surface and are connected
                    if (edgesShell != null)
                    {
                        if (edgesShell.FeatureFromLoops(connectedEdges, out IEnumerable<Face> featureFaces, out List<Face> connection, out bool isGap))
                        {
                            // there is a feature
                            AddFeatureProperties(vw, featureFaces, connection, isGap);
                        }
                    }
                }
                if (faces.Count > 0) // are there features defined by the selected faces?
                {
                    Shell facesShell = faces.First().Owner as Shell;
                    if (facesShell != null)
                    {
                        HashSet<Face> connectedFaces = Shell.ConnectedSameGeometryFaces(faces); // add all faces which have the same surface and are connected
                        if (facesShell.FeatureFromFaces(connectedFaces, out IEnumerable<Face> featureFaces, out List<Face> connection, out bool isGap))
                        {
                            // there is a feature. Multiple different features are not considered, we would need FeaturesFromFaces
                            // which would return more than one feature
                            AddFeatureProperties(vw, featureFaces, connection, isGap);
                        }
                    }
                }
                foreach (Face fc in faces)
                {   // build the menu with extrusions and rotations
                    AddExtensionProperties(fc, pa, vw);
                }

            }
        }

        private void AddExtensionProperties(Face fc, Projection.PickArea pa, IView vw)
        {
            FindExtrusionLoops(fc, pa, out List<ICurve[]> loopCurves, out List<Face[]> loopFaces, out List<Edge[]> loopEdges, out List<Plane> loopPlanes, out List<SimpleShape> loopShapes);
            for (int i = 0; i < loopCurves.Count; i++)
            {
                Face extrFace = Face.MakeFace(new PlaneSurface(loopPlanes[i]), loopShapes[i]);
                // in order to show an arrow on menu selection, we use the maximum extent of the selected face in direction of the plane
                Face toGetExtent = fc.Clone() as Face;
                toGetExtent.Modify(loopPlanes[i].CoordSys.GlobalToLocal);
                BoundingCube ext = toGetExtent.GetBoundingCube();
                GeoPoint zmax = loopPlanes[i].CoordSys.LocalToGlobal * new GeoPoint(0, 0, ext.Zmax);
                GeoPoint zmin = loopPlanes[i].CoordSys.LocalToGlobal * new GeoPoint(0, 0, ext.Zmin);
                Plane arrowPlane = new Plane(loopPlanes[i].Location, fc.Surface.GetNormal(fc.PositionOf(loopPlanes[i].Location)));
                GeoObjectList feedbackArrow = vw.Projection.MakeArrow(zmin, zmax, arrowPlane, Projection.ArrowMode.circleArrow);
                // which part of the face is used for meassurement? Try to fingd an edge or a vertex with the appropriate distance
                object maxObject = null, minObject = null;
                foreach (Edge edg in fc.Edges)
                {
                    if (Math.Abs(loopPlanes[i].Distance(edg.Vertex1.Position) - ext.Zmax) < Precision.eps && maxObject == null) maxObject = edg.Vertex1;
                    if (Math.Abs(loopPlanes[i].Distance(edg.Vertex1.Position) - ext.Zmin) < Precision.eps && minObject == null) minObject = edg.Vertex1;
                    if (Math.Abs(loopPlanes[i].Distance(edg.Vertex2.Position) - ext.Zmax) < Precision.eps && maxObject == null) maxObject = edg.Vertex2;
                    if (Math.Abs(loopPlanes[i].Distance(edg.Vertex2.Position) - ext.Zmin) < Precision.eps && minObject == null) minObject = edg.Vertex2;
                }
                if (minObject == null)
                {   // nothing found, maybe it is an edge like an arc, where we use the maximum or minimum distance
                    minObject = fc.Edges.MinBy(e => loopPlanes[i].Distance(e.Vertex1.Position)).Vertex1;
                }
                if (maxObject == null)
                {   // nothing found, maybe it is an edge like an arc, where we use the maximum or minimum distance
                    maxObject = fc.Edges.MinBy(e => -loopPlanes[i].Distance(e.Vertex1.Position)).Vertex1;
                }
                PropertyEntryDirectMenu extrudeMenu = new PropertyEntryDirectMenu("MenuId.ExtrusionLength");
                extrudeMenu.IsSelected = (selected, frame) =>
                {
                    if (selected)
                    {
                        currentMenuSelection.Clear();
                        currentMenuSelection.Add(extrFace);
                        currentMenuSelection.AddRange(feedbackArrow);
                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                    }
                    return true;
                };
                Face[] lfaces = loopFaces[i];
                Edge[] ledges = loopEdges[i];
                Plane lplane = loopPlanes[i];
                extrudeMenu.ExecuteMenu = (menuId) =>
                {
                    ParametricsExtrudeAction pea = new ParametricsExtrudeAction(minObject, maxObject, lfaces, ledges, lplane, selectAction.Frame);
                    selectAction.Frame.SetAction(pea);
                    return true;
                };
                this.subEntries.Add(extrudeMenu);
            }
        }

        /// <summary>
        /// Add the properties of a feature or detail of a solid to the list of modelling properties
        /// </summary>
        /// <param name="vw"></param>
        /// <param name="featureFaces">Faces of the shell, which belong to the feature</param>
        /// <param name="connection">Additional faces, which seperate the feature from the shell</param>
        /// <param name="isGap">Is a hole in the solid or part standing out</param>
        private void AddFeatureProperties(IView vw, IEnumerable<Face> featureFaces, List<Face> connection, bool isGap)
        {
            List<Face> ff = new List<Face>(featureFaces.Select(fc => fc.Clone() as Face));
            GeoPoint2D uv = ff[0].Area.GetSomeInnerPoint(); // testpoint for orientation
            GeoPoint xyz = ff[0].Surface.PointAt(uv);
            GeoVector normal = ff[0].Surface.GetNormal(uv);
            foreach (Face fc in connection)
            {
                fc.ReverseOrientation();
            }
            ff.AddRange(connection.Select(fc => fc.Clone() as Face));
            BoundingCube ext = BoundingCube.EmptyBoundingCube;
            ext.MinMax(ff);
            Shell.ConnectFaces(ff.ToArray(), Math.Max(Precision.eps, ext.Size * 1e-6));
            Shell feature = Shell.FromFaces(ff.ToArray());
            feature.AssertOutwardOrientation();
            uv = ff[0].Surface.PositionOf(xyz);
            bool wasInverse = normal * ff[0].Surface.GetNormal(uv) < 0;
#if DEBUG
            bool ok = feature.CheckConsistency();
#endif
            //double v = feature.Volume(ext.Size / 1000); // maybe it is totally flat
            //if (v < Precision.eps) return null; // Volume calculation is too slow
            Solid featureSolid = Solid.MakeSolid(feature.Clone() as Shell);
            Shell shell = featureFaces.First().Owner as Shell; // this is the original shell of the solid
            SimplePropertyGroup mh = new SimplePropertyGroup("MenuId.Feature");
            mh.StateChangedEvent += (s, e) =>
            {
                if (e.EventState == StateChangedArgs.State.Selected)
                {
                    currentMenuSelection.Clear();
                    currentMenuSelection.Add(feature);
                    vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                }
            };

            PropertyEntryDirectMenu copyFeature = new PropertyEntryDirectMenu("MenuId.Feature.CopyToClipboard");
            PropertyEntryDirectMenu positionFeature = new PropertyEntryDirectMenu("MenuId.Feature.Position");
            PropertyEntryDirectMenu nameFeature = new PropertyEntryDirectMenu("MenuId.Feature.Name");
            PropertyEntryDirectMenu removeFeature = new PropertyEntryDirectMenu("MenuId.Feature.Remove");
            PropertyEntryDirectMenu splitFeature = new PropertyEntryDirectMenu("MenuId.Feature.Split");
            mh.Add(new IPropertyEntry[] { copyFeature, positionFeature, nameFeature, removeFeature, splitFeature });
            // this is very rudimentary. We have to provide a version of ParametricsDistanceAction, where you can select from and to object. Only axis is implemented
            GeoObjectList fa = feature.FeatureAxis;
            Line axis = null;
            //foreach (IGeoObject geoObject in fa)
            //{
            //    Face axisOf = geoObject.UserData.GetData("CADability.AxisOf") as Face;
            //    if (axisOf != null)
            //    {
            //        if (featureI.Contains(axisOf))
            //        {
            //            axis = geoObject as Line;
            //        }
            //    }
            //    if (axis != null) break;
            //}
            copyFeature.ExecuteMenu = (menuId) =>
            {
                Solid sldToCopy = Solid.MakeSolid(feature.Clone() as Shell);
                selectAction.Frame.UIService.SetClipboardData(new GeoObjectList(sldToCopy), true);
                return true;
            };
            copyFeature.IsSelected = (selected, frame) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            positionFeature.ExecuteMenu = (frame) =>
            {
                ParametricsDistanceActionOld pd = new ParametricsDistanceActionOld(feature, selectAction.Frame);
                selectAction.Frame.SetAction(pd);
                return true;
            };
            positionFeature.IsSelected = (selected, frame) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            nameFeature.ExecuteMenu = (frame) =>
            {
                string name = shell.GetNewFeatureName();
                shell.AddFeature(name, featureFaces);
                if (shell.Owner is Solid sld)
                {
                    selectAction.SetSelectedObject(sld);
                    IPropertyEntry toEdit = selectAction.Frame.ControlCenter.FindItem(name);
                    if (toEdit != null)
                    {

                        List<IPropertyEntry> parents = new List<IPropertyEntry>();
                        if (toEdit != null)
                        {
                            IPropertyEntry p = toEdit;
                            while ((p = p.Parent as IPropertyEntry) != null)
                            {
                                parents.Add(p);
                            }
                            IPropertyPage propertyPage = parents[parents.Count - 1].Parent as IPropertyPage;
                            if (propertyPage != null)
                            {
                                for (int k = parents.Count - 1; k >= 0; --k)
                                {
                                    propertyPage.OpenSubEntries(parents[k], true);
                                }
                                toEdit.StartEdit(false);
                            }
                        }
                    }
                }
                return true;
            };
            nameFeature.IsSelected = (selected, frame) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            removeFeature.ExecuteMenu = (frame) =>
            {
                bool addRemoveOk = shell.AddAndRemoveFaces(connection, featureFaces);
                currentMenuSelection.Clear();
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            splitFeature.ExecuteMenu = (frame) =>
            {
                Solid solid = shell.Owner as Solid;
                Solid[] remaining = Solid.Subtract(solid, featureSolid);
                if (remaining != null && remaining.Length > 0)
                {
                    IGeoObjectOwner owner = solid.Owner;
                    owner.Remove(solid);
                    for (int i = 0; i < remaining.Length; ++i) owner.Add(remaining[i]);
                    owner.Add(featureSolid);
                }
                return true;
            };
        }
        /// <summary>
        /// There is a face <paramref name="fc"/> pointed at by <paramref name="pa"/>. We try to find a loop of curves on faces
        /// passing through pa. This loop msut be perpendicular to all edges it crosses, so that it could have been used 
        /// in an extrusion operation to build all faces, which are touched by the loop curves. To each loop curve there is a face,
        /// on which this curve resides. There may be several (or none) loops and sets of faces as a result. For each result there 
        /// is also a plane, in which all loop curves reside and which is the plane at which the shell can be split to extent or shrink 
        /// the shape along the edges.
        /// </summary>
        /// <param name="fc"></param>
        /// <param name="pa"></param>
        /// <param name="loopCurves"></param>
        /// <param name="loopFaces"></param>
        /// <param name="loopEdges"></param>
        /// <param name="loopPlanes"></param>
        /// <returns></returns>
        private void FindExtrusionLoops(Face fc, Projection.PickArea pa, out List<ICurve[]> loopCurves, out List<Face[]> loopFaces,
            out List<Edge[]> loopEdges, out List<Plane> loopPlanes, out List<SimpleShape> loopShapes)
        {
            loopCurves = new List<ICurve[]>();
            loopFaces = new List<Face[]>();
            loopEdges = new List<Edge[]>();
            loopPlanes = new List<Plane>();
            loopShapes = new List<SimpleShape>();
            List<Plane> planeCandidates = new List<Plane>();
            List<Edge> edgeCandidates = new List<Edge>();
            // find the point, which was hit by the mouse
            GeoPoint[] ips = fc.GetLineIntersection(pa.FrontCenter, pa.Direction);
            if (ips.Length > 0)
            {
                GeoPoint pointOnFace = ips.MinBy(p => (p | pa.FrontCenter)); // this is the point on the face, defined by the mouse position (and closest to the viewer)
                Shell shell = fc.Owner as Shell;
                // now find edges on the outline of the face, which are candidates for extrusion, i.e. are lines and the direction is a extrusion direction of the face
                List<GeoVector> directions = new List<GeoVector>();
                foreach (Edge edge in fc.OutlineEdges)
                {
                    if (edge.Curve3D is Line line && fc.Surface.IsExtruded(line.StartDirection))
                    {
                        if (!directions.Exists(d => Precision.SameDirection(d, line.StartDirection, false)))
                        {
                            directions.Add(line.StartDirection);
                        }
                    }
                }
                // we create an octtree of the edges of the shell to have fast acces to the edges from a point
                OctTree<EdgeInOctTree> edgeOctTree = new OctTree<EdgeInOctTree>(shell.GetExtent(0.0), Precision.eps);
                edgeOctTree.AddMany(shell.Edges.Select(e => new EdgeInOctTree(e)));

                for (int i = 0; i < directions.Count; i++) // all possible extrusion directions found for the face fc
                {
                    Plane plane = new Plane(pointOnFace, directions[i]); // the plane perpendicular to the possible extrusion direction
                    PlaneSurface planeSurface = new PlaneSurface(plane); // the same plane as a surface
                    ICurve[] intCrvs = shell.GetPlaneIntersection(planeSurface); // it would be better, if GetPlaneIntersection could return the faces and edges, which are intersected
                    ICurve2D[] intCrvs2D = new ICurve2D[intCrvs.Length];
                    for (int j = 0; j < intCrvs.Length; j++)
                    {
                        intCrvs2D[j] = planeSurface.GetProjectedCurve(intCrvs[j], 0.0);
                    }
                    CompoundShape cs = CompoundShape.CreateFromList(intCrvs2D, Precision.eps);
                    if (cs == null) continue;
                    for (int j = 0; j < cs.SimpleShapes.Length; ++j)
                    {
                        Border.Position pos = cs.SimpleShapes[j].GetPosition(GeoPoint2D.Origin, Precision.eps);
                        bool validShape = true;
                        if (pos == Border.Position.OnCurve)
                        {   // There must be a SimpleShape which passes through (0,0), since the plane's location is on the face
                            // The point (0,0) might also reside on a hole, which is no problem.
                            List<ICurve> crvs = new List<ICurve>();
                            List<Edge> edges = new List<Edge>();
                            foreach (ICurve2D curve2D in cs.SimpleShapes[j].Segments)
                            {
                                crvs.Add(planeSurface.Make3dCurve(curve2D));
                                GeoPoint pointOnEdge = planeSurface.PointAt(curve2D.EndPoint);
                                EdgeInOctTree[] eo = edgeOctTree.GetObjectsFromPoint(pointOnEdge);
                                Edge edgeFound = null;
                                foreach (Edge edg in eo.Select(e => e.Edge))
                                {
                                    if (edg.Curve3D is Line line)
                                    {
                                        if ((line as ICurve).DistanceTo(planeSurface.PointAt(curve2D.EndPoint)) < Precision.eps &&
                                            Precision.SameDirection(line.StartDirection, directions[i], false))
                                        {
                                            edgeFound = edg;
                                            break;
                                        }
                                    }
                                }
                                if (edgeFound != null) { edges.Add(edgeFound); }
                                else
                                {
                                    validShape = false;
                                    break;
                                }
                            }
                            if (validShape)
                            {   // all vertices of the simpleshape have a corresponding edge
                                HashSet<Face> hFaces = new HashSet<Face>();
                                foreach (Edge edg in edges)
                                {
                                    hFaces.Add(edg.PrimaryFace);
                                    hFaces.Add(edg.SecondaryFace);
                                }
                                loopCurves.Add(crvs.ToArray());
                                loopFaces.Add(hFaces.ToArray());
                                loopEdges.Add(edges.ToArray());
                                loopPlanes.Add(plane);
                                loopShapes.Add(cs.SimpleShapes[j]);
                            }
                        }
                        else
                        {
                            validShape = false;
                        }
                    }
                }
            }
        }
    }
}
