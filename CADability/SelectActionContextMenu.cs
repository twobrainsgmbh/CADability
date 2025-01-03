using CADability.Actions;
using CADability.Attribute;
using CADability.GeoObject;
using CADability.Substitutes;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Wintellect.PowerCollections;
using static CADability.Actions.SelectObjectsAction;
using CADability.Curve2D;
using CADability.Shapes;
#if WEBASSEMBLY
using CADability.WebDrawing;
using Point = CADability.WebDrawing.Point;
#else
using System.Drawing;
using Point = System.Drawing.Point;
#endif

namespace CADability
{
    class SelectActionContextMenu : ICommandHandler
    {
        private SelectObjectsAction selectAction;
        private List<Face> faces;
        private List<Shell> shells;
        private List<Solid> solids;
        private List<(IEnumerable<Face> faces, IEnumerable<Face> lids, bool isGap)> features;
        private GeoObjectList curves;
        private List<Edge> edges;
        private GeoObjectList currentMenuSelection;
        private IPaintTo3DList displayList;
        private IView currentView;
        public SelectActionContextMenu(SelectObjectsAction soa)
        {
            this.selectAction = soa;
            currentMenuSelection = new GeoObjectList();
        }
        public void FilterMouseMessages(MouseAction mouseAction, MouseEventArgs e, IView vw, ref bool handled)
        {
            if (mouseAction == MouseAction.MouseUp && e.Button == MouseButtons.Right)
            {
                handled = true;
                if (vw.AllowContextMenu)
                    ShowContextMenu(e.Location, vw);
            }
        }
        private void ShowContextMenu(Point mousePoint, IView vw)
        {
            // Here the context menu for the right mouse click is composed
            // We arrive here in two different situations: 
            // - nothing was selected before and the right clickt happened on a face or an edge or some other object
            // - several edges or faces have been selected with the menu "select more faces" or "select more edges"
            // depending on the situation, different menus are beeing generated. The menu items are connected to the respective
            // menu handlers
            currentView = vw;
            GeoObjectList selectedObjects = selectAction.GetSelectedObjects();
            List<MenuWithHandler> cm = new List<MenuWithHandler>(); // build a context menue for the accumulation case

            HashSet<Edge> selectedEdges = new HashSet<Edge>();
            HashSet<Face> selectedFaces = new HashSet<Face>();
            if (selectAction.IsAccumulating)
            {   // we have been accumulating edges or faces, now we want to do something with this collection
                // this right click should not add or remove some more objects to or from the selection
                // but try to find a "feature" and show a menu to handle it
                foreach (IGeoObject go in selectedObjects)
                {
                    if (go is ICurve && go.Owner is Edge edg) selectedEdges.Add(edg);
                    if (go is Face fc) selectedFaces.Add(fc);
                    // multiple selected axis or curves are not implemented yet.
                }
                if (selectedEdges.Any())
                {
                    Shell edgesShell = null;
                    if (selectedEdges.Count > 0) edgesShell = (selectedEdges.First().Owner as Face).Owner as Shell;
                    selectedEdges = Shell.connectedSameGeometryEdges(selectedEdges); // add all faces which have the same surface and are connected
                    if (edgesShell != null)
                    {
                        if (edgesShell.FeatureFromLoops(selectedEdges, out IEnumerable<Face> featureFaces, out List<Face> connection, out bool isGap))
                        {
                            // there is a feature
                            cm.Add(CreateFeatureMenu(vw, featureFaces, connection, isGap));
                        }
                    }
                }
                if (selectedFaces.Any())
                {
                    Shell facesShell = null;
                    if (selectedFaces.Count > 0) facesShell = selectedFaces.First().Owner as Shell;
                    if (facesShell != null)
                    {
                        selectedFaces = Shell.connectedSameGeometryFaces(selectedFaces); // add all faces which have the same surface and are connected
                        if (facesShell.FeatureFromFaces(selectedFaces, out IEnumerable<Face> featureFaces, out List<Face> connection, out bool isGap))
                        {
                            // there is a feature. Multiple different features are not considered, we would need FeaturesFromFaces
                            // which would return more than one feature
                            cm.Add(CreateFeatureMenu(vw, featureFaces, connection, isGap));
                        }
                    }
                }
                selectAction.StopAccumulate();
            }
            else
            {
                // we are not in accumulation mode. Nothing is selected when the right click occurred
                int pickRadius = selectAction.Frame.GetIntSetting("Select.PickRadius", 5);
                Projection.PickArea pa = vw.Projection.GetPickSpace(new Rectangle(mousePoint.X - pickRadius, mousePoint.Y - pickRadius, pickRadius * 2, pickRadius * 2));
                IActionInputView pm = vw as IActionInputView;
                GeoObjectList fl = vw.Model.GetObjectsFromRect(pa, new Set<Layer>(pm.GetVisibleLayers()), PickMode.singleFaceAndCurve, null); // returns all the face under the cursor

                List<Face> faces = new List<Face>();
                List<Shell> shells = new List<Shell>(); // only Shells, which are not part of a Solid
                List<Solid> solids = new List<Solid>();
                List<Face> axis = new List<Face>();
                //private List<(IEnumerable<Face> faces, IEnumerable<Face> lids, bool isGap)> features;
                List<Edge> edges = new List<Edge>();

                for (int i = 0; i < fl.Count; i++)
                {
                    if (fl[i] is Face fc)
                    {
                        faces.Add(fc);
                        if (fc.Owner is Shell sh)
                        {
                            if (sh.Owner is Solid solid) solids.Add(solid);
                            else shells.Add(sh);
                        }
                    }
                    if (fl[i] is ICurve)
                    {
                        if (fl[i].Owner is Edge edge) edges.Add(edge);
                        if (fl[i].UserData.Contains("CADability.AxisOf"))
                        {
                            Face faceWithAxis = (fl[i]).UserData.GetData("CADability.AxisOf") as Face;
                            if (faceWithAxis != null) axis.Add(faceWithAxis);
                        }
                    }
                }
                foreach (Face fc in faces)
                {
                    cm.Add(CreateFaceMenu(selectAction, vw, fc));
                }
                foreach (Edge edg in edges)
                {
                    cm.Add(CreateEdgeMenu(selectAction, vw, edg));
                }
                foreach (Solid sld in solids)
                {
                    cm.Add(CreateSolidMenu(selectAction, vw, sld));
                }
            }
            cm.AddRange(CreateGeneralContextMenus(vw)); // the menu "Show"
            vw.SetPaintHandler(PaintBuffer.DrawingAspect.Select, new PaintView(OnRepaintSelect));
            mousePoint.X += vw.DisplayRectangle.Width / 8; // find a better place for the menu position, using the extent of the objects
            vw.Canvas.ShowContextMenu(cm.ToArray(), mousePoint, ContextMenuCollapsed);

            /*
            int pickRadius = soa.Frame.GetIntSetting("Select.PickRadius", 5);
            Projection.PickArea pa = vw.Projection.GetPickSpace(new Rectangle(mousePoint.X - pickRadius, mousePoint.Y - pickRadius, pickRadius * 2, pickRadius * 2));
            IActionInputView pm = vw as IActionInputView;
            GeoObjectList fl = vw.Model.GetObjectsFromRect(pa, new Set<Layer>(pm.GetVisibleLayers()), PickMode.onlyFaces, null); // returns all the face under the cursor
            // in most cases there is only a single face, which is of interest, only when we have two solids with same or overlapping faces
            // and one of them is not selectable without also selecting the other, we want both.
            faces = new List<Face>();
            shells = new List<Shell>(); // only Shells, which are not part of a Solid
            solids = new List<Solid>();
            features = new List<(IEnumerable<Face> faces, IEnumerable<Face> lids, bool isGap)>();
            double delta = vw.Model.Extent.Size * 1e-4;
            double mindist = double.MaxValue;
            for (int i = 0; i < fl.Count; i++)
            {
                if (fl[i] is Face face) // this should always be the case
                {
                    double z = face.Position(pa.FrontCenter, pa.Direction, 0);
                    if (z < mindist)
                    {
                        if (z < mindist - delta) faces.Clear();
                        faces.Add(face);
                        mindist = z;
                    }
                }
            }
            HashSet<Edge> relevantEdges = new HashSet<Edge>();
            for (int i = 0; i < faces.Count; i++)
            {
                relevantEdges.UnionWith(faces[i].AllEdges);
                if (faces[i].Owner is Shell shell)
                {
                    if (shell.Owner is Solid sld) { if (!solids.Contains(sld)) solids.Add(sld); }
                    else { if (!shells.Contains(shell)) shells.Add(shell); }
                }
            }
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i].Owner is Shell shell)
                {
                    for (int j = 0; j < faces[i].OutlineEdges.Length; j++)
                    {
                        Edge edg = faces[i].OutlineEdges[j];
                        if (edg.IsPartOfHole(edg.OtherFace(faces[i])))
                        {
                            Face otherFace = edg.OtherFace(faces[i]);
                            if (otherFace != null)
                            {
                                Edge[] hole = otherFace.GetHole(edg);
                                List<Edge[]> loops = new List<Edge[]> { hole };
                                List<IEnumerable<Face>> lfeatures = new List<IEnumerable<Face>>();
                                List<IEnumerable<Face>> lids = new List<IEnumerable<Face>>();
                                HashSet<Face> startWith = new HashSet<Face>();
                                foreach (Edge edge in hole)
                                {
                                    startWith.Add(edge.OtherFace(otherFace)); // all faces that are connected to the hole in "otherFace"
                                }
                                shell.FeaturesFromEdges(loops, lfeatures, lids, startWith);
                                for (int k = 0; k < lfeatures.Count; k++)
                                {
                                    // if (lfeatures[k].Count() < shell.Faces.Length * 0.5) features.Add(lfeatures[k]);
                                }
                            }
                        }
                    }
                }
            }
            curves = vw.Model.GetObjectsFromRect(pa, new Set<Layer>(pm.GetVisibleLayers()), PickMode.onlyEdges, null); // returns only edges
            edges = new List<Edge>();
            // we only accept edges, which belong to one of the selected faces
            for (int i = curves.Count - 1; i >= 0; --i)
            {
                if (curves[i].Owner is Edge edge)
                {
                    if (relevantEdges.Contains(edge)) edges.Add(edge);
                    else
                    {
                        double z = curves[i].Position(pa.FrontCenter, pa.Direction, 0);
                        if (z - delta < mindist) edges.Add(edge); // this is an edge in front of the closest face
                    }
                    curves.Remove(i);
                }
                else
                {
                    double z = curves[i].Position(pa.FrontCenter, pa.Direction, 0);
                    if (z - delta > mindist) curves.Remove(i);
                }
            }

            // now we have curves, edges, faces, shells and solids (text, hatch, block, dimension not yet implemented) from the right click

            // we also need access to the already selected edges and faces to 
            GeoObjectList selobj = soa.GetSelectedObjects();
            foreach (IGeoObject go in selobj)
            {
                if (go is ICurve && go.Owner is Edge edg) selectedEdges.Add(edg);
                if (go is Face fc) selectedFaces.Add(fc);
            }
            if (selectedEdges.Any())
            {
                Shell edgesShell = null;
                if (selectedEdges.Count > 0) edgesShell = (selectedEdges.First().Owner as Face).Owner as Shell;
                if (edgesShell != null)
                {
                    if (edgesShell.FeatureFromLoops(selectedEdges, out IEnumerable<Face> featureFaces, out List<Face> connection, out bool isGap))
                    {
                        features.Add((featureFaces, connection, isGap));
                    }
                }

            }
            List<MenuWithHandler> cm = new List<MenuWithHandler>();

            Shell owningShell = null;
            if (edges.Count > 0) owningShell = (edges[0].Owner as Face).Owner as Shell;
            if (owningShell == null && faces.Count > 0) owningShell = (faces[0].Owner as Shell);
            if (owningShell == null && selectedFaces.Count > 0) owningShell = selectedFaces.First().Owner as Shell;
            if (owningShell == null && selectedEdges.Count > 0) owningShell = (selectedEdges.First().Owner as Face).Owner as Shell;
            if (owningShell != null)
            {
                if (selectedEdges.Count > 0)
                {
                    List<Shell> shellFeatures = owningShell.FeaturesFromEdges(selectedEdges);
                }
                for (int i = 0; i < features.Count; i++)
                {

                }
            }
            for (int i = 0; i < curves.Count; i++)
            {   // No implementation for simple curves yet.
                // We would need a logical connection structure as in vertex->edge->face->shell in 2d curves, which we currently do not have.
                Face faceWithAxis = (curves[i] as IGeoObject).UserData.GetData("CADability.AxisOf") as Face;
                if (faceWithAxis != null)
                {
                    MenuWithHandler mh = new MenuWithHandler("MenuId.Axis");
                    mh.SubMenus = GetFacesSubmenus(faceWithAxis).ToArray();
                    mh.Target = this;
                    IGeoObject curveI = curves[i] as IGeoObject;
                    mh.OnSelected = (m, selected) =>
                    {
                        currentMenuSelection.Clear();
                        currentMenuSelection.Add(curveI);
                        currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                    };
                    cm.Add(mh);
                }
            }
            for (int i = 0; i < edges.Count; i++)
            {
                MenuWithHandler mh = new MenuWithHandler();
                mh.ID = "MenuId.Edge." + i.ToString();
                mh.Text = StringTable.GetString("MenuId.Edge", StringTable.Category.label);
                List<MenuWithHandler> lmh = new List<MenuWithHandler>();
                MenuWithHandler selectMoreEdges = new MenuWithHandler("MenuId.SelectMoreEdges");
                lmh.Add(selectMoreEdges);
                Edge edgeI = edges[i];
                selectMoreEdges.OnCommand = (menuId) =>
                {
                    soa.SetSelectedObject(edgeI.Curve3D as IGeoObject);
                    soa.Accumulate(PickMode.onlyEdges);
                    return true;
                };
                lmh.AddRange(edges[i].GetContextMenu(soa.Frame));
                mh.SubMenus = lmh.ToArray();
                mh.Target = this;
                cm.Add(mh);
            }
            for (int i = 0; i < faces.Count; i++)
            {
                MenuWithHandler mh = new MenuWithHandler();
                mh.ID = "MenuId.Face." + i.ToString();
                mh.Text = StringTable.GetString("MenuId.Face", StringTable.Category.label);
                List<MenuWithHandler> lmh = new List<MenuWithHandler>();
                MenuWithHandler selectMoreFaces = new MenuWithHandler("MenuId.SelectMoreFaces");
                lmh.Add(selectMoreFaces);
                Face faceI = faces[i];
                selectMoreFaces.OnCommand = (menuId) =>
                {
                    soa.SetSelectedObject(faceI);
                    soa.Accumulate(PickMode.onlyFaces);
                    return true;
                };
                lmh.AddRange(GetFacesSubmenus(faces[i]));
                if (owningShell != null)
                {
                    double thickness = owningShell.GetGauge(faces[i], out HashSet<Face> frontSide, out HashSet<Face> backSide);
                    if (thickness != double.MaxValue && frontSide.Count > 0)
                    {
                        MenuWithHandler gauge = new MenuWithHandler("MenuId.Gauge");
                        gauge.OnCommand = (menuId) =>
                        {
                            ParametricsOffset po = new ParametricsOffset(frontSide, backSide, soa.Frame, thickness);
                            soa.Frame.SetAction(po);
                            return true;
                        };
                        gauge.OnSelected = (menuId, selected) =>
                        {
                            currentMenuSelection.Clear();
                            currentMenuSelection.AddRange(frontSide.ToArray());
                            currentMenuSelection.AddRange(backSide.ToArray());
                            currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                        };
                        lmh.Add(gauge);
                    }
                    int n = owningShell.GetFaceDistances(faces[i], out List<Face> distanceTo, out List<double> distance, out List<GeoPoint> pointsFrom, out List<GeoPoint> pointsTo);
                    for (int j = 0; j < n; j++)
                    {
                        if (backSide == null || !backSide.Contains(distanceTo[j])) // this is not already used as gauge
                        {
                            HashSet<Face> capturedFaceI = new HashSet<Face>(new Face[] { faces[i] });
                            HashSet<Face> capturedDistTo = new HashSet<Face>(new Face[] { distanceTo[j] });
                            double capturedDistance = distance[j];
                            GeoPoint capturedPoint1 = pointsFrom[j];
                            GeoPoint capturedPoint2 = pointsTo[j];
                            GeoObjectList feedbackArrow = currentView.Projection.MakeArrow(pointsFrom[j], pointsTo[j], currentView.Projection.ProjectionPlane, Projection.ArrowMode.circleArrow);
                            MenuWithHandler faceDist = new MenuWithHandler("MenuId.FaceDistance");
                            faceDist.OnCommand = (menuId) =>
                            {
                                ParametricsDistanceAction pd = new ParametricsDistanceAction(capturedFaceI, capturedDistTo, capturedPoint1, capturedPoint2, soa.Frame);
                                soa.Frame.SetAction(pd);
                                return true;
                            };
                            faceDist.OnSelected = (menuId, selected) =>
                            {
                                currentMenuSelection.Clear();
                                currentMenuSelection.AddRange(capturedFaceI.ToArray());
                                currentMenuSelection.AddRange(capturedDistTo.ToArray());
                                currentMenuSelection.AddRange(feedbackArrow);
                                currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                            };
                            lmh.Add(faceDist);
                        }
                    }
                }
                mh.SubMenus = lmh.ToArray();
                mh.Target = this;
                cm.Add(mh);

                //cm.AddRange(GetFacesSubmenus(faces));
            }
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i].Owner is Shell shell)
                {
                    Shell sh = shell.FeatureFromFace(faces[i]);
                }
            }
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].PrimaryFace.Owner is Shell shell)
                {
                    //shell.FeatureFromEdges(edges[i].PrimaryFace);
                }
            }
            for (int i = 0; i < shells.Count; i++)
            {
                MenuWithHandler mh = new MenuWithHandler();
                mh.ID = "MenuId.Shell." + i.ToString();
                mh.Text = StringTable.GetString("MenuId.Shell", StringTable.Category.label);
                mh.SubMenus = shells[i].GetContextMenu(soa.Frame);
                mh.Target = this;
                cm.Add(mh);
            }
            for (int i = 0; i < solids.Count; i++)
            {
                MenuWithHandler mh = new MenuWithHandler();
                mh.ID = "MenuId.Solid." + i.ToString();
                mh.Text = StringTable.GetString("MenuId.Solid", StringTable.Category.label);
                mh.SubMenus = solids[i].GetContextMenu(soa.Frame);
                mh.Target = this;
                cm.Add(mh);
            }
            cm.AddRange(CreateGeneralContextMenus(vw));
            vw.SetPaintHandler(PaintBuffer.DrawingAspect.Select, new PaintView(OnRepaintSelect));
            mousePoint.X += vw.DisplayRectangle.Width / 8; // find a better place for the menu position, using the extent of the objects
            vw.Canvas.ShowContextMenu(cm.ToArray(), mousePoint, ContextMenuCollapsed);
            */
        }

        private MenuWithHandler CreateSolidMenu(SelectObjectsAction selectAction, IView vw, Solid sld)
        {
            MenuWithHandler mh = new MenuWithHandler();
            mh.ID = "MenuId.Solid";
            mh.Text = StringTable.GetString("MenuId.Solid", StringTable.Category.label);
            mh.SubMenus = sld.GetContextMenu(selectAction.Frame); // not consistent: solid context menu created in Solid
            mh.Target = this;
            mh.OnSelected = (menuId, selected) =>
            {   // show the provided solid as feedback
                currentMenuSelection.Clear();
                currentMenuSelection.Add(sld);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            return mh;
        }

        private MenuWithHandler CreateEdgeMenu(SelectObjectsAction selectAction, IView vw, Edge edg)
        {
            MenuWithHandler mh = new MenuWithHandler();
            mh.ID = "MenuId.Edge";
            mh.Text = StringTable.GetString("MenuId.Edge", StringTable.Category.label);
            List<MenuWithHandler> lmh = new List<MenuWithHandler>();
            MenuWithHandler selectMoreEdges = new MenuWithHandler("MenuId.SelectMoreEdges");
            lmh.Add(selectMoreEdges);
            HashSet<Edge> edges = Shell.connectedSameGeometryEdges(new Edge[] { edg });
            mh.OnSelected = (menuId, selected) =>
            {   // show the provided edge and the "same geometry connected" edges as feedback
                currentMenuSelection.Clear();
                currentMenuSelection.AddRange(edges.Select((edge) => edge.Curve3D as IGeoObject));
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            selectMoreEdges.OnCommand = (menuId) =>
            {
                selectAction.SetSelectedObjects(new GeoObjectList(edges.Select((edge) => edge.Curve3D as IGeoObject)));
                selectAction.Accumulate(PickMode.onlyEdges);
                return true;
            };
            MenuWithHandler mhdist = new MenuWithHandler();
            mhdist.ID = "MenuId.Parametrics.DistanceTo";
            mhdist.Text = StringTable.GetString("MenuId.Parametrics.DistanceTo", StringTable.Category.label);
            mhdist.Target = new ParametricsDistanceActionOld(edg, selectAction.Frame);
            lmh.Add(mhdist);

            mh.SubMenus = lmh.ToArray();
            return mh;
        }

        private MenuWithHandler CreateFaceMenu(SelectObjectsAction selectAction, IView vw, Face fc)
        {
            MenuWithHandler mh = new MenuWithHandler();
            mh.ID = "MenuId.Face";
            mh.Text = StringTable.GetString("MenuId.Face", StringTable.Category.label);
            List<MenuWithHandler> lmh = new List<MenuWithHandler>();
            MenuWithHandler selectMoreFaces = new MenuWithHandler("MenuId.SelectMoreFaces");
            lmh.Add(selectMoreFaces);
            Face faceI = fc;
            HashSet<Face> faces = Shell.connectedSameGeometryFaces(new Face[] { fc });
            selectMoreFaces.OnCommand = (menuId) =>
            {
                selectAction.SetSelectedObjects(new GeoObjectList(faces.ToArray()));
                selectAction.Accumulate(PickMode.onlyFaces);
                return true;
            };
            lmh.AddRange(GetFacesSubmenus(fc));
            mh.OnSelected = (menuId, selected) =>
            {   // show the provided face and the "same geometry connected" faces as feedback
                currentMenuSelection.Clear();
                currentMenuSelection.AddRange(faces.ToArray());
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            if (fc.Owner is Shell owningShell)
            {
                double thickness = owningShell.GetGauge(fc, out HashSet<Face> frontSide, out HashSet<Face> backSide);
                if (thickness != double.MaxValue && thickness > 0.0 && frontSide.Count > 0)
                {
                    MenuWithHandler gauge = new MenuWithHandler("MenuId.Gauge");
                    gauge.OnCommand = (menuId) =>
                    {
                        ParametricsOffset po = new ParametricsOffset(frontSide, backSide, selectAction.Frame, thickness);
                        selectAction.Frame.SetAction(po);
                        return true;
                    };
                    gauge.OnSelected = (menuId, selected) =>
                    {
                        currentMenuSelection.Clear();
                        currentMenuSelection.AddRange(frontSide.ToArray());
                        currentMenuSelection.AddRange(backSide.ToArray());
                        currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                    };
                    lmh.Add(gauge);
                }
                int n = owningShell.GetFaceDistances(fc, out List<Face> distanceTo, out List<double> distance, out List<GeoPoint> pointsFrom, out List<GeoPoint> pointsTo);
                for (int j = 0; j < n; j++)
                {
                    if (backSide == null || !backSide.Contains(distanceTo[j])) // this is not already used as gauge
                    {
                        HashSet<Face> capturedFaceI = new HashSet<Face>(new Face[] { fc });
                        HashSet<Face> capturedDistTo = new HashSet<Face>(new Face[] { distanceTo[j] });
                        double capturedDistance = distance[j];
                        GeoPoint capturedPoint1 = pointsFrom[j];
                        GeoPoint capturedPoint2 = pointsTo[j];
                        GeoObjectList feedbackArrow = currentView.Projection.MakeArrow(pointsFrom[j], pointsTo[j], currentView.Projection.ProjectionPlane, Projection.ArrowMode.circleArrow);
                        MenuWithHandler faceDist = new MenuWithHandler("MenuId.FaceDistance");
                        faceDist.OnCommand = (menuId) =>
                        {
                            ParametricsDistanceAction pd = new ParametricsDistanceAction(capturedFaceI, capturedDistTo, capturedPoint1, capturedPoint2, selectAction.Frame);
                            selectAction.Frame.SetAction(pd);
                            return true;
                        };
                        faceDist.OnSelected = (menuId, selected) =>
                        {
                            currentMenuSelection.Clear();
                            currentMenuSelection.AddRange(capturedFaceI.ToArray());
                            currentMenuSelection.AddRange(capturedDistTo.ToArray());
                            currentMenuSelection.AddRange(feedbackArrow);
                            currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                        };
                        lmh.Add(faceDist);
                    }
                }
            }
            mh.SubMenus = lmh.ToArray();
            return mh;
        }
        /// <summary>
        /// Create a submenu for a feature. This enables the user to name, remove (and add to clipboard) the feature, or splt the solid 
        /// into feature and remaining parts
        /// </summary>
        /// <param name="vw"></param>
        /// <param name="featureFaces"></param>
        /// <param name="connection"></param>
        /// <param name="isGap"></param>
        /// <returns></returns>
        private MenuWithHandler CreateFeatureMenu(IView vw, IEnumerable<Face> featureFaces, List<Face> connection, bool isGap)
        {
            List<Face> ff = new List<Face>(featureFaces.Select(fc => fc.Clone() as Face));
            foreach (Face fc in connection)
            {
                fc.ReverseOrientation();
            }
            ff.AddRange(connection.Select(fc => fc.Clone() as Face));
            BoundingCube ext = BoundingCube.EmptyBoundingCube;
            ext.MinMax(ff);
            Shell.connectFaces(ff.ToArray(), Math.Max(Precision.eps, ext.Size * 1e-6));
            Shell feature = Shell.FromFaces(ff.ToArray());
#if DEBUG
            bool ok = feature.CheckConsistency();
#endif
            Solid featureSolid = Solid.MakeSolid(feature);
            Shell shell = featureFaces.First().Owner as Shell; // this is the original shell of the solid
            MenuWithHandler mh = new MenuWithHandler("MenuId.Feature");
            mh.OnSelected = (m, selected) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            MenuWithHandler positionFeature = new MenuWithHandler("MenuId.Feature.Position");
            MenuWithHandler nameFeature = new MenuWithHandler("MenuId.Feature.Name");
            MenuWithHandler removeFeature = new MenuWithHandler("MenuId.Feature.Remove");
            MenuWithHandler splitFeature = new MenuWithHandler("MenuId.Feature.Split");
            mh.SubMenus = new MenuWithHandler[] { positionFeature, nameFeature, removeFeature, splitFeature };
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
            positionFeature.OnCommand = (menuId) =>
            {
                ParametricsDistanceActionOld pd = new ParametricsDistanceActionOld(feature, selectAction.Frame);
                selectAction.Frame.SetAction(pd);
                return true;
            };
            positionFeature.OnSelected = (menuId, selected) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            nameFeature.OnCommand = (menuId) =>
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
            nameFeature.OnSelected = (menuId, selected) =>
            {
                currentMenuSelection.Clear();
                currentMenuSelection.Add(feature);
                currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
            };
            removeFeature.OnCommand = (menuId) =>
            {
                Solid solid = shell.Owner as Solid;
                Solid[] remaining = Solid.Subtract(solid, featureSolid);
                if (remaining != null && remaining.Length > 0)
                {
                    IGeoObjectOwner owner = solid.Owner;
                    owner.Remove(solid);
                    for (int i = 0; i < remaining.Length; ++i) owner.Add(remaining[i]);
                }
                return true;
            };
            splitFeature.OnCommand = (menuId) =>
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
            return mh;
        }

        private IEnumerable<MenuWithHandler> CreateGeneralContextMenus(IView vw)
        {
            // selection independent menu items
            MenuWithHandler mh = new MenuWithHandler();
            mh.ID = "MenuId.Show";
            mh.Text = StringTable.GetString("MenuId.Show", StringTable.Category.label);
            MenuWithHandler mhShowHidden = new MenuWithHandler();
            mhShowHidden.ID = "MenuId.ShowHidden";
            mhShowHidden.Text = StringTable.GetString("MenuId.ShowHidden", StringTable.Category.label);
            mhShowHidden.OnCommand = (menuId) =>
            {
                foreach (IGeoObject geoObject in vw.Model)
                {
                    if (geoObject.Layer != null && geoObject.Layer.Name == "CADability.Hidden")
                    {
                        Layer layer = geoObject.UserData.GetData("CADability.OriginalLayer") as Layer;
                        if (layer != null) geoObject.Layer = layer;
                    }
                }
                return true;
            };
            MenuWithHandler mhShowAxis = new MenuWithHandler();
            mhShowAxis.ID = "MenuId.ShowAxis";
            mhShowAxis.Text = StringTable.GetString("MenuId.ShowAxis", StringTable.Category.label);
            mhShowAxis.OnCommand = (menuId) =>
            {
                bool isOn = false;
                foreach (IGeoObject geoObject in vw.Model)
                {
                    Shell shell = null;
                    if (geoObject is Solid solid) shell = solid.Shells[0];
                    else if (geoObject is Shell sh) shell = sh;
                    if (shell != null && shell.FeatureAxis.Count > 0)
                    {
                        isOn = shell.FeatureAxis[0].IsVisible;
                        break;
                    }
                }

                using (vw.Canvas.Frame.Project.Undo.UndoFrame)
                {
                    foreach (IGeoObject geoObject in vw.Model)
                    {
                        if (geoObject is Solid solid) solid.ShowFeatureAxis = !isOn;
                        else if (geoObject is Shell shell) shell.ShowFeatureAxis = !isOn;
                    }
                }
                return true;
            };
            mhShowAxis.OnUpdateCommand = (menuId, commandState) =>
            {
                bool isOn = false;
                foreach (IGeoObject geoObject in vw.Model)
                {
                    Shell shell = null;
                    if (geoObject is Solid solid) shell = solid.Shells[0];
                    else if (geoObject is Shell sh) shell = sh;
                    if (shell != null && shell.FeatureAxis.Count > 0)
                    {
                        isOn = shell.FeatureAxis[0].IsVisible;
                        break;
                    }
                }
                commandState.Checked = isOn;
                return true;
            };
            mh.SubMenus = new MenuWithHandler[] { mhShowHidden, mhShowAxis };
            mh.Target = this;
            return new[] { mh };
        }

        private class FeatureCommandHandler : MenuWithHandler, ICommandHandler
        {
            public Face[] involvedFaces;
            SelectActionContextMenu selectActionContextMenu;
            public FeatureCommandHandler(Face[] involvedFaces, SelectActionContextMenu selectActionContextMenu, string ID) : base(ID)
            {
                this.involvedFaces = involvedFaces;
                this.selectActionContextMenu = selectActionContextMenu;
            }
            bool ICommandHandler.OnCommand(string MenuId)
            {
                switch (MenuId)
                {
                    case "MenuId.Fillet.ChangeRadius":
                        ParametricsRadius pr = new ParametricsRadius(involvedFaces, selectActionContextMenu.selectAction.Frame, true);
                        selectActionContextMenu.selectAction.Frame.SetAction(pr);
                        return true;
                    case "MenuId.Fillet.Remove":
                        Shell orgShell = involvedFaces[0].Owner as Shell;
                        if (orgShell != null)
                        {
                            RemoveFillet rf = new RemoveFillet(involvedFaces[0].Owner as Shell, new HashSet<Face>(involvedFaces));
                            Shell sh = rf.Result();
                            if (sh != null)
                            {
                                using (selectActionContextMenu.selectAction.Frame.Project.Undo.UndoFrame)
                                {
                                    sh.CopyAttributes(orgShell);
                                    IGeoObjectOwner owner = orgShell.Owner;
                                    owner.Remove(orgShell);
                                    owner.Add(sh);
                                }
                            }
                        }
                        return true;
                }
                return false;
            }

            void ICommandHandler.OnSelected(MenuWithHandler selectedMenuItem, bool selected)
            {
                selectActionContextMenu.currentMenuSelection.Clear();
                selectActionContextMenu.currentMenuSelection.AddRange(involvedFaces);
                selectActionContextMenu.currentView.Invalidate(PaintBuffer.DrawingAspect.Select, selectActionContextMenu.currentView.DisplayRectangle);
            }

            bool ICommandHandler.OnUpdateCommand(string MenuId, CommandState CommandState)
            {
                return true;
            }
        }
        private List<MenuWithHandler> GetFacesSubmenus(Face face)
        {
            List<MenuWithHandler> res = new List<MenuWithHandler>();
            if (face.IsFillet())
            {
                HashSet<Face> connectedFillets = new HashSet<Face>();
                CollectConnectedFillets(face, connectedFillets);
                FeatureCommandHandler fch = new FeatureCommandHandler(connectedFillets.ToArray(), this, "MenuId.Fillet");
                MenuWithHandler fr = new MenuWithHandler("MenuId.Fillet.ChangeRadius");
                fr.OnCommand = (menuId) =>
                {
                    ParametricsRadius pr = new ParametricsRadius(connectedFillets.ToArray(), selectAction.Frame, true);
                    selectAction.Frame.SetAction(pr);
                    return true;
                };
                fr.OnSelected = (mh, selected) =>
                {
                    currentMenuSelection.Clear();
                    currentMenuSelection.AddRange(connectedFillets.ToArray());
                    currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                };
                MenuWithHandler fd = new MenuWithHandler("MenuId.Fillet.Remove");
                fd.OnCommand = (menuId) =>
                {
                    Face[] involvedFaces = connectedFillets.ToArray();
                    Shell orgShell = involvedFaces[0].Owner as Shell;
                    if (orgShell != null)
                    {
                        RemoveFillet rf = new RemoveFillet(involvedFaces[0].Owner as Shell, new HashSet<Face>(involvedFaces));
                        Shell sh = rf.Result();
                        if (sh != null)
                        {
                            using (selectAction.Frame.Project.Undo.UndoFrame)
                            {
                                sh.CopyAttributes(orgShell);
                                IGeoObjectOwner owner = orgShell.Owner;
                                owner.Remove(orgShell);
                                owner.Add(sh);
                            }
                        }
                    }
                    selectAction.StopAccumulate();
                    return true;
                };
                fd.OnSelected = (mh, selected) =>
                {
                    currentMenuSelection.Clear();
                    currentMenuSelection.AddRange(connectedFillets.ToArray());
                    currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                };
                fch.SubMenus = new MenuWithHandler[] { fr, fd };
                res.Add(fch);
            }
            IEnumerable<Face> connected = face.GetSameSurfaceConnected();

            // if (connected.Any())
            {
                List<Face> lconnected = new List<Face>(connected);
                lconnected.Add(face);
                BoundingCube ext = BoundingCube.EmptyBoundingCube;
                foreach (Face fc in connected)
                {
                    ext.MinMax(fc.GetExtent(0.0));
                }
                // maybe a full sphere, cone, cylinder or torus:
                // except for the sphere: position axis
                // except for the cone: change radius or diameter
                // for the cone: smaller and larger diameter
                // for cone and cylinder: total length
                if (face.Surface is CylindricalSurface || face.Surface is CylindricalSurfaceNP || face.Surface is ToroidalSurface)
                {
                    MenuWithHandler mh = new MenuWithHandler("MenuId.FeatureDiameter");
                    mh.OnCommand = (menuId) =>
                    {
                        ParametricsRadius pr = new ParametricsRadius(lconnected.ToArray(), selectAction.Frame, false);
                        selectAction.Frame.SetAction(pr);
                        return true;
                    };
                    mh.OnSelected = (menuId, selected) =>
                    {
                        currentMenuSelection.Clear();
                        currentMenuSelection.AddRange(lconnected.ToArray());
                        currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                    };
                    res.Add(mh);
                }
                if (face.Surface is CylindricalSurface || face.Surface is CylindricalSurfaceNP || face.Surface is ConicalSurface)
                {
                    Line axis = null;

                    if (face.Surface is ICylinder cyl) axis = cyl.Axis.Clip(ext);
                    if (face.Surface is ConicalSurface cone) axis = cone.AxisLine(face.Domain.Bottom, face.Domain.Top);
                    MenuWithHandler mh = new MenuWithHandler("MenuId.AxisPosition");
                    mh.OnCommand = (menuId) =>
                    {
                        ParametricsDistanceAction pd = new ParametricsDistanceAction(lconnected, axis, selectAction.Frame);
                        selectAction.Frame.SetAction(pd);
                        return true;
                    };
                    mh.OnSelected = (menuId, selected) =>
                    {
                        currentMenuSelection.Clear();
                        currentMenuSelection.AddRange(lconnected.ToArray());
                        currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                    };
                    res.Add(mh);
                }
            }
            if (face.Surface is PlaneSurface pls)
            {
                // try to find parallel outline edges to modify the distance
                Edge[] outline = face.OutlineEdges;
                for (int j = 0; j < outline.Length - 1; j++)
                {
                    for (int k = j + 1; k < outline.Length; k++)
                    {
                        if (outline[j].Curve3D is Line l1 && outline[k].Curve3D is Line l2)
                        {
                            if (Precision.SameDirection(l1.StartDirection, l2.StartDirection, false))
                            {
                                // two parallel outline lines, we could parametrize the distance
                                Edge o1 = outline[j];
                                Edge o2 = outline[k]; // outline[i] is not captured correctly for the anonymous method. I don't know why. With local copies, it works.
                                double lmin = double.MaxValue;
                                double lmax = double.MinValue;
                                double p = Geometry.LinePar(l1.StartPoint, l1.EndPoint, l2.StartPoint);
                                lmin = Math.Min(lmin, p);
                                lmax = Math.Max(lmax, p);
                                p = Geometry.LinePar(l1.StartPoint, l1.EndPoint, l2.EndPoint);
                                lmin = Math.Max(Math.Min(lmin, p), 0);
                                lmax = Math.Min(Math.Max(lmax, p), 1);
                                GeoPoint p1 = Geometry.LinePos(l1.StartPoint, l1.EndPoint, (lmin + lmax) / 2.0);
                                GeoPoint p2 = Geometry.DropPL(p1, l2.StartPoint, l2.EndPoint);
                                if ((p1 | p2) > Precision.eps)
                                {
                                    MenuWithHandler mh = new MenuWithHandler("MenuId.EdgeDistance");
                                    Line feedback = Line.TwoPoints(p1, p2);
                                    GeoObjectList feedbackArrow = currentView.Projection.MakeArrow(p1, p2, pls.Plane, Projection.ArrowMode.circleArrow);
                                    mh.OnCommand = (menuId) =>
                                    {
                                        ParametricsDistanceActionOld pd = new ParametricsDistanceActionOld(o1, o2, feedback, pls.Plane, selectAction.Frame);
                                        selectAction.Frame.SetAction(pd);
                                        return true;
                                    };
                                    mh.OnSelected = (m, selected) =>
                                    {
                                        currentMenuSelection.Clear();
                                        currentMenuSelection.AddRange(feedbackArrow);
                                        currentMenuSelection.Add(o1.OtherFace(face));
                                        currentMenuSelection.Add(o2.OtherFace(face));
                                        currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
                                    };
                                    res.Add(mh);
                                }
                            }
                        }
                    }
                }
            }
            if (res.Count > 6)
            {
                List<MenuWithHandler> lm = new List<MenuWithHandler>();
                for (int i = 4; i < res.Count; i++)
                {
                    lm.Add(res[i]);
                }
                res.RemoveRange(4, lm.Count);
                MenuWithHandler subMenu = new MenuWithHandler("MenuId.More");
                subMenu.SubMenus = lm.ToArray();
                res.Add(subMenu);
            }
            return res;
        }

        private void CollectConnectedFillets(Face face, HashSet<Face> connectedFillets)
        {
            if (!connectedFillets.Contains(face))
            {
                connectedFillets.Add(face);
                if (face.Surface is ISurfaceOfArcExtrusion extrusion)
                {
                    foreach (Edge edge in face.OutlineEdges)
                    {
                        Face otherFace = edge.OtherFace(face);
                        if (otherFace.IsFillet() && otherFace.Surface is ISurfaceOfArcExtrusion otherExtrusion && Precision.IsEqual(extrusion.Radius, otherExtrusion.Radius))
                        {
                            // if (edge.Curve2D(face).DirectionAt(0.5).IsMoreHorizontal == extrusion.ExtrusionDirectionIsV && otherFace.Surface is ISurfaceOfArcExtrusion arcExtrusion)
                            {
                                CollectConnectedFillets(otherFace, connectedFillets);
                            }
                        }
                        else if (otherFace.Surface is SphericalSurface ss && Precision.IsEqual(ss.RadiusX, extrusion.Radius))
                        {
                            CollectConnectedFillets(otherFace, connectedFillets);
                        }
                    }
                }
                else if (face.Surface is SphericalSurface ss)
                {   // at a sphere a fillet might branch out
                    foreach (Edge edge in face.OutlineEdges)
                    {
                        Face otherFace = edge.OtherFace(face);
                        if (edge.IsTangentialEdge() && otherFace.IsFillet() && otherFace.Surface is ISurfaceOfArcExtrusion otherExtrusion && Precision.IsEqual(ss.RadiusX, otherExtrusion.Radius))
                        {
                            CollectConnectedFillets(otherFace, connectedFillets);
                        }

                    }
                }
            }
        }

        private void OnRepaintSelect(Rectangle IsInvalid, IView View, IPaintTo3D PaintToSelect)
        {
            PaintToSelect.PushState();
            bool oldSelect = PaintToSelect.SelectMode;
            PaintToSelect.SelectMode = true;
            PaintToSelect.SelectColor = Color.FromArgb(128, Color.Yellow);
            //PaintToSelect.SetColor (Color.Yellow);
            //PaintToSelect.UseZBuffer(false);
            int wobbleWidth = -1; // i.e. also show faces which are behind other faces
            if ((PaintToSelect.Capabilities & PaintCapabilities.ZoomIndependentDisplayList) != 0)
            {
                PaintToSelect.OpenList("select-context");
                foreach (IGeoObject go in currentMenuSelection)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                displayList = PaintToSelect.CloseList();
                PaintToSelect.SelectedList(displayList, wobbleWidth);
                //PaintToSelect.List(displayList);
            }
            PaintToSelect.SelectMode = oldSelect;
            PaintToSelect.PopState();
        }

        private void ContextMenuCollapsed(int dumy)
        {
            currentView.RemovePaintHandler(PaintBuffer.DrawingAspect.Select, new PaintView(OnRepaintSelect));
            currentMenuSelection.Clear();
            currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
        }
        bool ICommandHandler.OnCommand(string MenuId)
        {
            return true;
        }

        void ICommandHandler.OnSelected(MenuWithHandler selectedMenu, bool selected)
        {   // not used any more, handled by each menu item directely
            currentMenuSelection.Clear();
            if (selectedMenu is FeatureCommandHandler fch)
            {
                currentMenuSelection.AddRange(fch.involvedFaces);
            }
            else if (selectedMenu.ID.StartsWith("MenuId.Curve."))
            {
                int ind = int.Parse(selectedMenu.ID.Substring("MenuId.Curve.".Length));
                currentMenuSelection.Add(curves[ind]);
            }
            else if (selectedMenu.ID.StartsWith("MenuId.Edge."))
            {
                int ind = int.Parse(selectedMenu.ID.Substring("MenuId.Edge.".Length));
                currentMenuSelection.Add(edges[ind].Curve3D as IGeoObject);
            }
            else if (selectedMenu.ID.StartsWith("MenuId.Face."))
            {
                int ind = int.Parse(selectedMenu.ID.Substring("MenuId.Face.".Length));
                currentMenuSelection.Add(faces[ind]);
            }
            else if (selectedMenu.ID.StartsWith("MenuId.Shell."))
            {
                int ind = int.Parse(selectedMenu.ID.Substring("MenuId.Shell.".Length));
                currentMenuSelection.Add(shells[ind]);
            }
            else if (selectedMenu.ID.StartsWith("MenuId.Solid."))
            {
                int ind = int.Parse(selectedMenu.ID.Substring("MenuId.Solid.".Length));
                currentMenuSelection.Add(solids[ind]);
            }
            else currentMenuSelection.Clear();
            currentView.Invalidate(PaintBuffer.DrawingAspect.Select, currentView.DisplayRectangle);
        }

        bool ICommandHandler.OnUpdateCommand(string MenuId, CommandState CommandState)
        {
            CommandState.Enabled = true;
            CommandState.Checked = false;
            return true;
        }

        private void ShowContextMenu(Point pos)
        {
        }
    }
}
