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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.VisualStyles;
using System.Xml.Linq;
using Wintellect.PowerCollections;
using static CADability.Projection;

namespace ShapeIt
{
    /// <summary>
    /// Class to enable Edges to reside in an <see cref="OctTree{T}"/>
    /// </summary>
    class EdgeInOctTree : IOctTreeInsertable
    {
        public Edge Edge { get; private set; }
        public EdgeInOctTree(Edge edge) { Edge = edge; }

        public BoundingCube GetExtent(double precision)
        {
            return Edge.Curve3D.GetExtent();
        }

        public bool HitTest(ref BoundingCube cube, double precision)
        {
            return Edge.Curve3D.HitTest(cube);
        }

        public bool HitTest(Projection projection, BoundingRect rect, bool onlyInside)
        {
            throw new NotImplementedException();
        }

        public bool HitTest(Projection.PickArea area, bool onlyInside)
        {
            throw new NotImplementedException();
        }

        public double Position(GeoPoint fromHere, GeoVector direction, double precision)
        {
            throw new NotImplementedException();
        }
    }
    internal class ModellingPropertyEntries : PropertyEntryImpl
    {
        private List<IPropertyEntry> subEntries = new List<IPropertyEntry>(); // the list of property entries in this property page
        private IFrame cadFrame; // the frame (root object of cadability)
        private SelectObjectsAction selectAction; // the select action, which runs parallel to the modelling property page
        private bool isAccumulating; // expect more clicks to add same objects of a certain type (edges, faces)
        private List<IGeoObject> accumulatedObjects = new List<IGeoObject>(); // objects beeing accumulated
        private bool modelligIsActive; // if true, there is no normal or "old" CADability selection, but every mouseclick of the SelectObjectsAction is handled here
        private Feedback feedback;
        private PickArea lastPickArea; // for ComposeModellingEntries to avoid passing it to all methods
        public ModellingPropertyEntries(IFrame cadFrame) : base("Modelling.Properties")
        {
            this.cadFrame = cadFrame;
            selectAction = cadFrame.ActiveAction as SelectObjectsAction;
            if (selectAction != null)
            {
                selectAction.SelectedObjectListChangedEvent += SelectedObjectListChanged;
                selectAction.ShowSelectedObjects = false; // we do the selection display with this class not with the SelectObjectsAction
            }
            cadFrame.ActionTerminatedEvent += ActionTerminated;
            cadFrame.ActionStartedEvent += ActionStarted;

            isAccumulating = false;
            cadFrame.ViewsChangedEvent += ViewsChanged;
            modelligIsActive = true;

            feedback = new Feedback();
            feedback.Attach(cadFrame.ActiveView);
            FeedbackArrow.SetNumberFormat(cadFrame);
        }

        private void SelectedObjectListChanged(SelectObjectsAction sender, GeoObjectList selectedObjects)
        {
            IEnumerable<Layer> visiblaLayers = new List<Layer>();
            if (cadFrame.ActiveView is ModelView mv) visiblaLayers = mv.GetVisibleLayers();
            GeoObjectList singleObjects = cadFrame.ActiveView.Model.GetObjectsFromRect(sender.GetPickArea(), new Set<Layer>(visiblaLayers), PickMode.singleFaceAndCurve, null); // returns all the faces or edges under the cursor
            ComposeModellingEntries(singleObjects, cadFrame.ActiveView, sender.GetPickArea());
            // if (cadFrame.UIService.ModifierKeys == Keys.Shift)
            if (modelligIsActive)
            {
                propertyPage?.BringToFront();
                cadFrame.SetControlCenterFocus("Modelling", "Modelling.Properties", true, false);
            }
            IsOpen = true;
            Refresh();

        }
        #region CADability events
        private void ViewsChanged(IFrame theFrame)
        {
            cadFrame = theFrame;
            selectAction = cadFrame.ActiveAction as SelectObjectsAction; // which should always be the case
            if (selectAction != null)
            {
                selectAction.FilterMouseMessagesEvent -= FilterSelectMouseMessages; // no problem, if it wasn't set
                selectAction.FilterMouseMessagesEvent += FilterSelectMouseMessages;
            }
            feedback.Attach(theFrame.ActiveView);
            if (modelligIsActive)
            {
                propertyPage?.BringToFront();
                cadFrame.SetControlCenterFocus("Modelling", "Modelling.Properties", true, false);
            }
        }
        private void ActionStarted(CADability.Actions.Action action)
        {
            if (modelligIsActive)
            {
                Select(); // selects this entry, which is the top entry for the page. By this, the current selected entry is beeing unselected and the display
                          // of feedback objects is removed
                feedback.Clear(); // additionally, but probably not necessary, because unselect of the current entry did the same
                cadFrame.ActiveView.Invalidate(PaintBuffer.DrawingAspect.Select, cadFrame.ActiveView.DisplayRectangle);
                subEntries.Clear();
                IPropertyPage pp = propertyPage;
                if (pp != null)
                {
                    pp.Remove(this); // to reflect this newly composed entry
                    pp.Add(this, true);
                }
            }
        }
        private void ActionTerminated(CADability.Actions.Action action)
        {
            if (modelligIsActive)
            {
                cadFrame.ControlCenter.ShowPropertyPage("Modelling");
            }
        }
        private void FilterSelectMouseMessages(SelectObjectsAction.MouseAction mouseAction, CADability.Substitutes.MouseEventArgs e, IView vw, ref bool handled)
        {
            if (modelligIsActive)
            {
                // TODO: other mouse activities: cursor, drag rect etc.
                if (mouseAction == SelectObjectsAction.MouseAction.MouseUp && e.Button == CADability.Substitutes.MouseButtons.Left)
                {
                    GeoObjectList objectsUnderCursor = GetObjectsUnderCursor(e.Location, vw, out Projection.PickArea pickArea);
                    ComposeModellingEntries(objectsUnderCursor, vw, pickArea);
                    IsOpen = true;
                    Refresh();
                    handled = true;
                }
            }
        }
        private void StopAccumulating()
        {
            IGeoObject[] capturedOjbects = accumulatedObjects.ToArray(); // a list captured for the undo method
            cadFrame.Project.Undo.AddUndoStep(new ReversibleChange(() =>
            {
                accumulatedObjects.Clear();
                accumulatedObjects.AddRange(capturedOjbects);
                isAccumulating = true;
                return true;
            }));
            isAccumulating = false;
            accumulatedObjects.Clear();
        }
        internal bool OnEscape()
        {
            if (accumulatedObjects.Count > 1 && isAccumulating)
            {
                StopAccumulating();
                IPropertyPage pp = propertyPage;
                if (pp != null)
                {
                    pp.SelectEntry(this); // to unselect the currently selected entry and clear feedback objects
                    subEntries.Clear();
                    pp.Remove(this); // to reflect this newly composed entry
                    pp.Add(this, true);
                }
                return true;
            }
            return false;
        }

        #endregion
        #region PropertyEntry implementation
        public override PropertyEntryType Flags => PropertyEntryType.Selectable | PropertyEntryType.GroupTitle | PropertyEntryType.Checkable | PropertyEntryType.HasSubEntries; // PropertyEntryType.ContextMenu | 
        public override IPropertyEntry[] SubItems => subEntries.ToArray();
        public override string Value
        {
            get
            {
                // Value == "0"(not checked), "1"(checked), "2" (indetermined or disabled)
                return modelligIsActive ? "1" : "0";
            }
        }
        public override void ButtonClicked(PropertyEntryButton button)
        {
            if (button == PropertyEntryButton.check)
            {
                modelligIsActive = !modelligIsActive;
                propertyPage?.Refresh(this);
                cadFrame.ActionStack.FindAction(typeof(SelectObjectsAction));
                if (cadFrame.ActiveAction is SelectObjectsAction selectAction) selectAction.ShowSelectedObjects = !modelligIsActive;
            }
        }
        public override void Selected(IPropertyEntry previousSelected)
        {
            feedback.Clear();
            cadFrame.ActiveView.Invalidate(PaintBuffer.DrawingAspect.Select, cadFrame.ActiveView.DisplayRectangle);
            base.Selected(previousSelected);
        }
        #endregion
        private GeoObjectList GetObjectsUnderCursor(System.Drawing.Point mousePoint, IView vw, out Projection.PickArea pickArea)
        {
            GeoObjectList objectsUnderCursor = new GeoObjectList();
            int pickRadius = selectAction.Frame.GetIntSetting("Select.PickRadius", 5);
            pickArea = vw.Projection.GetPickSpace(new Rectangle(mousePoint.X - pickRadius, mousePoint.Y - pickRadius, pickRadius * 2, pickRadius * 2));
            IEnumerable<Layer> visiblaLayers = new List<Layer>();
            if (vw is ModelView mv) visiblaLayers = mv.GetVisibleLayers();
            return vw.Model.GetObjectsFromRect(pickArea, new Set<Layer>(visiblaLayers), PickMode.singleFaceAndCurve, null); // returns all the faces or edges under the cursor
        }
        private void ComposeModellingEntries(GeoObjectList objectsUnderCursor, IView vw, PickArea pickArea)
        {   // a mouse left button up took place. Compose all the entries for objects, which can be handled by 
            // the object(s) under the mouse cursor
            lastPickArea = pickArea;
            subEntries.Clear(); // build a new list of modelling properties
            Axis clickBeam = new Axis(pickArea.FrontCenter, pickArea.Direction);
            if (isAccumulating && accumulatedObjects.Count > 0)
            {
                // we are in accumulation mode. 
                if (objectsUnderCursor.Count == 0)
                {   // clicked on empty space: clear accumulated objects, but you can undo this
                    StopAccumulating();
                }
                for (int i = 0; i < objectsUnderCursor.Count; i++)
                {
                    if (((accumulatedObjects[0] is ICurve && objectsUnderCursor[i] is ICurve) && (accumulatedObjects[0].Owner.GetType() == objectsUnderCursor[i].Owner.GetType())) // both are curves or both are edges
                        || accumulatedObjects[0].GetType() == objectsUnderCursor[i].GetType())
                    {   // if this object is already in the accumulated objects, remove it, else add it
                        if (accumulatedObjects.Contains(objectsUnderCursor[i])) accumulatedObjects.Remove(objectsUnderCursor[i]);
                        else accumulatedObjects.Add(objectsUnderCursor[i]);
                    }
                }

            }
            // handle either accumulated objects or the objects under the cursor
            if (accumulatedObjects.Count > 1)
            {   // there must be at least 2 objects of the same type be accumulated to show actions for this group
                // the first accumulated object tells which type of object we are accumulation
                SimplePropertyGroup title = null;
                SimplePropertyGroup collection = null;
                Type accumulatingType = accumulatedObjects[0].GetType(); // Face, Edge, ICurve
                if (accumulatedObjects[0] is ICurve)
                {
                    if (accumulatedObjects[0].Owner is Edge) accumulatingType = typeof(Edge);
                    else accumulatingType = typeof(ICurve);
                }
                if (accumulatingType == typeof(Edge))
                {
                    title = new SimplePropertyGroup("Accumulating.Edges");
                    collection = new SimplePropertyGroup("Accumulating.Edges.List");
                }
                else if (accumulatingType == typeof(ICurve))
                {
                    title = new SimplePropertyGroup("Accumulating.Curves");
                    collection = new SimplePropertyGroup("Accumulating.Curves.List");
                }
                else if (accumulatingType == typeof(Face))
                {
                    title = new SimplePropertyGroup("Accumulating.Faces");
                    collection = new SimplePropertyGroup("Accumulating.faces.List");
                }
                subEntries.Add(title);
                title.Add(collection);
                DirectMenuEntry stopAccumulating = new DirectMenuEntry("Accumulating.Stop");
                stopAccumulating.ExecuteMenu = (frame) =>
                {
                    StopAccumulating();
                    return true;
                };
                title.Add(stopAccumulating);
                for (int i = 0; i < accumulatedObjects.Count; i++)
                {
                    collection.Add(accumulatedObjects[i].GetShowProperties(cadFrame));
                }
                if (accumulatingType == typeof(ICurve))
                {
                    List<ICurve> curves = accumulatedObjects.OfType<ICurve>().ToList();
                    // check the condition to make a ruled solid:
                    // two closed curves not in the same plane
                    List<Path> paths = Path.FromSegments(curves);
                    // if we have two paths which are flat but not in the same plane, we could make a ruled solid directly
                    // if we need more user control, e.g. specifying synchronous points on each path, we woould need a more
                    // sophisticated action
                    if (paths.Count == 2 && paths[0].GetPlanarState() == PlanarState.Planar && paths[1].GetPlanarState() == PlanarState.Planar)
                    {
                        if (!Precision.IsEqual(paths[0].GetPlane(), paths[1].GetPlane()) && paths[0].IsClosed && paths[1].IsClosed)
                        {
                            try
                            {
                                Solid sld = Make3D.MakeRuledSolid(paths[0], paths[1], cadFrame.Project);
                                if (sld != null)
                                {
                                    DirectMenuEntry makeRuledSolid = new DirectMenuEntry("MenuId.Constr.Solid.RuledSolid");
                                    makeRuledSolid.IsSelected = (selected, frame) =>
                                    {
                                        feedback.Clear();
                                        if (selected) feedback.FrontFaces.Add(sld);
                                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                                        return true;
                                    };
                                    makeRuledSolid.ExecuteMenu = (frame) =>
                                    {
                                        frame.Project.GetActiveModel().Add(sld);
                                        StopAccumulating();
                                        return true;
                                    };
                                    title.Add(makeRuledSolid);
                                }
                            }
                            catch (NotImplementedException) { }
                        }
                    }
                    if (paths.Count == 1 && paths[0].IsClosed && paths[0].GetPlanarState() == PlanarState.Planar)
                    {
                        Plane pln = paths[0].GetPlane();
                        Face fc = Face.MakeFace(new GeoObjectList(paths[0]));
                        if (fc != null)
                        {
                            DirectMenuEntry extrude = new DirectMenuEntry("MenuId.Constr.Solid.FaceExtrude"); // too bad, no icon yet, would be 159
                            extrude.ExecuteMenu = (frame) =>
                            {
                                frame.SetAction(new Constr3DFaceExtrude(fc));
                                StopAccumulating();
                                return true;
                            };
                            title.Add(extrude);
                            DirectMenuEntry rotate = new DirectMenuEntry("MenuId.Constr.Solid.FaceRotate"); // too bad, no icon yet, would be 160
                            rotate.ExecuteMenu = (frame) =>
                            {
                                frame.SetAction(new Constr3DFaceRotate(new GeoObjectList(fc)));
                                StopAccumulating();
                                return true;
                            };
                            title.Add(rotate);
                        }
                    }
                }
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
                Dictionary<Solid, List<Face>> solidToFaces = new Dictionary<Solid, List<Face>>();
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
                            if (sh.Owner is Solid solid)
                            {
                                solids.Add(solid);
                                if (!solidToFaces.TryGetValue(solid, out List<Face> fcs)) solidToFaces[solid] = fcs = new List<Face>();
                                fcs.Add(fc);
                            }
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
                {   // add menus for dealing with face dimensions
                    AddFaceProperties(vw, fc, clickBeam);
                }
                foreach (Edge edg in edges)
                {
                    AddEdgeProperties(vw, edg, clickBeam);
                }
                foreach (Solid sld in solids)
                {
                    AddSolidProperties(vw, sld, solidToFaces.TryGetValue(sld, out List<Face> found) ? found : null);
                }
                if (curves.Count > 1)
                {
                    if (CanMakePath(curves)) AddMakePath(vw, curves);
                }
                bool suppresRuledSolid = false;
                if (curves.Count == 2 && curves[0] is Path path1 && curves[1] is Path path2)
                {
                    if (Constr3DRuledSolid.ruledSolidTest(path1, path2))
                    {
                        DirectMenuEntry ruledSolid = new DirectMenuEntry("MenuId.Constr.Solid.RuledSolid");
                        ruledSolid.ExecuteMenu = (frame) =>
                            {
                                Constr3DRuledSolid.ruledSolidDo(path1, path2, selectAction.Frame);
                                return true;
                            };
                        ruledSolid.IsSelected = (selected, frame) =>
                        {
                            if (selected)
                            {
                                feedback.Clear();
                                for (int i = 0; i < curves.Count; i++)
                                {
                                    feedback.FrontFaces.Add(curves[i] as IGeoObject);
                                }
                                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);

                            }
                            return true;
                        };
                        suppresRuledSolid = true;
                    }
                }
                for (int i = 0; i < curves.Count; i++)
                {
                    AddCurveProperties(vw, curves[i], suppresRuledSolid);
                }

            }

            IPropertyPage pp = propertyPage;
            if (pp != null)
            {
                // pp.Refresh(this); // doesn't do the job, so we must remove and add
                pp.Remove(this); // to reflect this newly composed entry
                pp.Add(this, true);
                for (int i = 0; i < subEntries.Count; i++)
                {
                    if (subEntries[i].Flags.HasFlag(PropertyEntryType.HasSubEntries) && subEntries[i].SubItems.Length > 0) pp.OpenSubEntries(subEntries[i], true);
                }
                for (int i = 0; i < subEntries.Count; ++i)
                {
                    pp.OpenSubEntries(subEntries[i], false);
                }
                // open and select according to this priority list:
                IPropertyEntry spe;
                if (null != (spe = FindSubItem("MenuId.Feature")))
                {
                    pp.OpenSubEntries(spe, true);
                    pp.SelectEntry(spe);
                }
                if (null != (spe = FindSubItem("MenuId.Solid")))
                {
                    pp.OpenSubEntries(spe, true);
                    pp.SelectEntry(spe);
                }
                else if (null != (spe = FindSubItem("MenuId.Face")))
                {
                    pp.OpenSubEntries(spe, true);
                    pp.SelectEntry(spe);
                }
                else if (null != (spe = FindSubItem("MenuId.Edge")))
                {
                    pp.OpenSubEntries(spe, true);
                    pp.SelectEntry(spe);
                }
                else
                {
                    if (subEntries.Count > 0)
                    {
                        pp.OpenSubEntries(subEntries[0], true);
                        pp.SelectEntry(subEntries[0]);
                    }
                }
            }
        }
        private void Clear()
        {
            IPropertyPage pp = propertyPage;
            if (pp != null)
            {
                IPropertyEntry selection = pp.GetCurrentSelection();
                if (selection != null)
                {
                    selection.UnSelected(selection); // to regenerate the feedback display
                                                     // and by passing selected as "previousSelected" parameter, they can only regenerate projection dependant feedback 
                }
                subEntries.Clear();
                pp.Remove(this); // to reflect this newly composed entry
                pp.Add(this, true);
            }
        }


        private void AddCurveProperties(IView vw, ICurve curve, bool suppresRuledSolid)
        {
            SelectEntry lmh = new SelectEntry("MenuId.CurveMenus", true);
            lmh.LabelText = (curve as IGeoObject).Description;
            lmh.IsSelected = (selected, frame) =>
            {
                feedback.Clear();
                if (selected) feedback.ShadowFaces.Add(curve as IGeoObject);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            if (curve.IsClosed && curve.GetPlanarState() == PlanarState.Planar)
            {
                Plane plane = curve.GetPlane();
                Face fc = Face.MakeFace(new GeoObjectList(curve as IGeoObject));
                if (fc != null)
                {
                    DirectMenuEntry extrude = new DirectMenuEntry("MenuId.Constr.Solid.FaceExtrude"); // too bad, no icon yet, would be 159
                    extrude.ExecuteMenu = (frame) =>
                    {
                        frame.SetAction(new Constr3DFaceExtrude(fc));
                        return true;
                    };
                    extrude.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected) feedback.ShadowFaces.Add(fc);
                        feedback.Refresh();
                        return true;
                    };
                    lmh.Add(extrude);
                    DirectMenuEntry rotate = new DirectMenuEntry("MenuId.Constr.Solid.FaceRotate"); // too bad, no icon yet, would be 160
                    rotate.ExecuteMenu = (frame) =>
                    {
                        frame.SetAction(new Constr3DFaceRotate(new GeoObjectList(fc)));
                        return true;
                    };
                    rotate.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected) feedback.ShadowFaces.Add(fc);
                        feedback.Refresh();
                        return true;
                    };
                    lmh.Add(rotate);
                }
                if (!suppresRuledSolid)
                {
                    DirectMenuEntry ruled = new DirectMenuEntry("MenuId.Constr.Solid.RuledSolid"); // too bad, no icon yet, would be 161
                    ruled.ExecuteMenu = (frame) =>
                    {
                        frame.SetAction(new Constr3DRuledSolid(new GeoObjectList(curve as IGeoObject), frame));
                        return true;
                    };
                    lmh.Add(ruled);
                }
            }
            if (lmh.SubEntriesCount > 0)
            {
                subEntries.Add(lmh);
            }
        }

        private void AddMakePath(IView vw, List<ICurve> curves)
        {
            DirectMenuEntry mp = new DirectMenuEntry("MenuId.Object.MakePath");
            mp.IsSelected = (selected, frame) =>
            {   // show the selected curves as feedback
                feedback.Clear();
                if (selected)
                {
                    for (int i = 0; i < curves.Count; i++)
                    {
                        feedback.FrontFaces.Add(curves[i] as IGeoObject);
                    }
                }
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            mp.ExecuteMenu = (frame) =>
            {
                using (frame.Project.Undo.UndoFrame)
                {
                    List<Path> created = Path.FromSegments(curves);
                    vw.Model.Add(created.ToArray());
                    selectAction.SetSelectedObjects(new GeoObjectList(created as IEnumerable<IGeoObject>));
                }
                return true;
            };
            subEntries.Add(mp);
        }

        private void AddSolidProperties(IView vw, Solid sld, List<Face> fromFaces)
        {
            SelectEntry solidMenus = new SelectEntry("MenuId.Solid", true); // the container for all menus or properties of the solid
            solidMenus.IsSelected = (selected, frame) =>
            {
                feedback.Clear();
                if (selected) feedback.ShadowFaces.Add(sld);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            if (fromFaces != null)
            {
                foreach (Face fc in fromFaces)
                {   // build the menu with extrusions and rotations
                    solidMenus.Add(AddExtensionProperties(fc, lastPickArea, vw).ToArray());
                }
            }
            Model owner = sld.Owner as Model;
            if (owner != null)
            {
                GeoObjectList fromBox = owner.GetObjectsFromBox(sld.GetExtent(0.0));
                List<Solid> otherSolids = new List<Solid>();
                for (int i = 0; i < fromBox.Count; i++)
                {
                    if (fromBox[i] is Solid solid && sld != solid) otherSolids.Add(solid);
                }
                if (otherSolids.Count > 0)
                {   // there are other solids close to this solid, it is not guaranteed that these other solids interfere with this solid 
                    Func<bool, IFrame, bool> IsSelected = (selected, frame) =>
                    {   // this is the standard selection behaviour for BRep operations
                        feedback.Clear();
                        if (selected)
                        {
                            feedback.ShadowFaces.Add(sld);
                            feedback.BackFaces.AddRange(otherSolids);
                        }
                        feedback.Refresh();
                        return true;
                    };
                    BRepOpWith bRepOp = new BRepOpWith(sld, otherSolids, cadFrame); // it is all implemented in BRepOpWith, so we use the ICommandHandler to forward it
                    DirectMenuEntry mhSubtractFrom = new DirectMenuEntry("MenuId.Solid.RemoveFrom");
                    mhSubtractFrom.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.RemoveFrom");
                        this.Clear();
                        return true;
                    };
                    mhSubtractFrom.IsSelected = IsSelected;
                    solidMenus.Add(mhSubtractFrom);
                    DirectMenuEntry mhSubtractFromAll = new DirectMenuEntry("MenuId.Solid.RemoveFromAll");
                    mhSubtractFromAll.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.RemoveFromAll");
                        this.Clear();
                        return true;
                    };
                    mhSubtractFromAll.IsSelected = IsSelected;
                    solidMenus.Add(mhSubtractFromAll);
                    DirectMenuEntry mhSubtractAllFromThis = new DirectMenuEntry("MenuId.Solid.RemoveAll");
                    mhSubtractAllFromThis.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.RemoveAll");
                        this.Clear();
                        return true;
                    };
                    mhSubtractAllFromThis.IsSelected = IsSelected;
                    solidMenus.Add(mhSubtractAllFromThis);
                    DirectMenuEntry mhUniteWith = new DirectMenuEntry("MenuId.Solid.UniteWith");
                    mhUniteWith.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.UniteWith");
                        return true;
                    };
                    mhUniteWith.IsSelected = IsSelected;
                    solidMenus.Add(mhUniteWith);
                    DirectMenuEntry mhUniteWithAll = new DirectMenuEntry("MenuId.Solid.UniteWithAll");
                    mhUniteWithAll.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.UniteWithAll");
                        this.Clear();
                        return true;
                    };
                    mhUniteWithAll.IsSelected = IsSelected;
                    solidMenus.Add(mhUniteWithAll);
                    DirectMenuEntry mhIntersectWith = new DirectMenuEntry("MenuId.Solid.IntersectWith");
                    mhIntersectWith.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.IntersectWith");
                        this.Clear();
                        return true;
                    };
                    mhIntersectWith.IsSelected = IsSelected;
                    solidMenus.Add(mhIntersectWith);
                    DirectMenuEntry mhSplitWith = new DirectMenuEntry("MenuId.Solid.SplitWith");
                    mhSplitWith.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.SplitWith");
                        this.Clear();
                        return true;
                    };
                    mhSplitWith.IsSelected = IsSelected;
                    solidMenus.Add(mhSplitWith);
                    DirectMenuEntry mhSplitWithAll = new DirectMenuEntry("MenuId.Solid.SplitWithAll");
                    mhSplitWithAll.ExecuteMenu = (frame) =>
                    {
                        (bRepOp as ICommandHandler).OnCommand("MenuId.Solid.SplitWithAll");
                        this.Clear();
                        return true;
                    };
                    mhSplitWithAll.IsSelected = IsSelected;
                    solidMenus.Add(mhSplitWithAll);
                }
            }
            if (sld.Layer != null && sld.Layer.Name == "CADability.Transparent")
            {
                DirectMenuEntry mhtr = new DirectMenuEntry("MenuId.MakeOpaque");
                mhtr.ExecuteMenu = (frame) =>
                {   // reset the layer of this solid
                    Layer layer = sld.UserData.GetData("CADability.OriginalLayer") as Layer;
                    if (layer != null)
                    {
                        sld.Layer = layer;
                        sld.UserData.RemoveUserData("CADability.OriginalLayer");
                    }
                    else
                    {
                        Style sldstl = frame.Project.StyleList.GetDefault(Style.EDefaultFor.Solids);
                        if (sldstl != null && sldstl.Layer != null) sld.Layer = sldstl.Layer;
                    }
                    return true;
                };
                solidMenus.Add(mhtr);
            }
            else
            {
                DirectMenuEntry mhtr = new DirectMenuEntry("MenuId.MakeTransparent");
                mhtr.ExecuteMenu = (frame) =>
                {   // reset the layer of this solid
                    sld.UserData.Add("CADability.OriginalLayer", sld.Layer);
                    Layer layer = frame.Project.LayerList.CreateOrFind("CADability.Transparent");
                    layer.Transparency = 128; // should be configurable
                    sld.Layer = layer;
                    return true;
                };
                solidMenus.Add(mhtr);
            }
            DirectMenuEntry mhhide = new DirectMenuEntry("MenuId.Solid.Hide"); // hide this solid
            mhhide.ExecuteMenu = (frame) =>
            {
                if (!sld.UserData.ContainsData("CADability.OriginalLayer"))
                {
                    if (sld.Layer == null)
                    {
                        Style sldstl = frame.Project.StyleList.GetDefault(Style.EDefaultFor.Solids);
                        if (sldstl != null && sldstl.Layer != null) sld.Layer = sldstl.Layer;
                    }
                    sld.UserData.Add("CADability.OriginalLayer", sld.Layer);
                }
                Layer layer = frame.Project.LayerList.CreateOrFind("CADability.Hidden");
                sld.Layer = layer;
                if (frame.ActiveView is ModelView mv) mv.SetLayerVisibility(layer, false);
                return true;
            };
            solidMenus.Add(mhhide);

            //MenuWithHandler mhsel = new MenuWithHandler();
            //mhsel.ID = "MenuId.Selection.Set";
            //mhsel.Text = StringTable.GetString("MenuId.Selection.Set", StringTable.Category.label);
            //mhsel.Target = new SetSelection(this, frame.ActiveAction as SelectObjectsAction);
            //solidMenus.Add(mhsel);
            //MenuWithHandler mhadd = new MenuWithHandler();
            //mhadd.ID = "MenuId.Selection.Add";
            //mhadd.Text = StringTable.GetString("MenuId.Selection.Add", StringTable.Category.label);
            //mhadd.Target = new SetSelection(this, frame.ActiveAction as SelectObjectsAction);
            //solidMenus.Add(mhadd);
            //MenuWithHandler mhremove = new MenuWithHandler();
            //mhremove.ID = "MenuId.Remove";
            //mhremove.Text = StringTable.GetString("MenuId.Remove", StringTable.Category.label);
            //mhremove.Target = SimpleMenuCommand.HandleCommand((menuId) =>
            //{
            //    Model model = Owner as Model;
            //    if (model != null) model.Remove(this);
            //    return true;
            //});
            //solidMenus.Add(mhremove);
            subEntries.Add(solidMenus);
        }

        private void AddEdgeProperties(IView vw, Edge edg, Axis clickBeam)
        {
            SelectEntry edgeMenus = new SelectEntry("MenuId.Edge", true); // the container for all edge related menus or properties
            HashSet<Edge> edges = Shell.ConnectedSameGeometryEdges(new Edge[] { edg });
            edgeMenus.IsSelected = (selected, frame) =>
            {   // show the provided edge and the "same geometry connected" edges as feedback
                feedback.Clear();
                if (selected) feedback.FrontFaces.AddRange(edges.Select((edge) => edge.Curve3D as IGeoObject));
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };

            DirectMenuEntry selectMoreEdges = new DirectMenuEntry("MenuId.SelectMoreEdges");
            selectMoreEdges.ExecuteMenu = (frame) =>
            {
                accumulatedObjects.Add(edg.Curve3D as IGeoObject);
                isAccumulating = true;
                return true;
            };
            selectMoreEdges.IsSelected = (selected, frame) =>
            {   // show the provided face and the "same geometry connected" faces as feedback
                feedback.Clear();
                if (selected) feedback.FrontFaces.Add(edg.Curve3D as IGeoObject);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            edgeMenus.Add(selectMoreEdges);
            Shell owningShell = edg.PrimaryFace.Owner as Shell;
            DirectMenuEntry mhdist = new DirectMenuEntry("MenuId.Parametrics.DistanceTo");
            // mhdist.Target = new ParametricsDistanceActionOld(edg, selectAction.Frame); 
            // TODO!
            edgeMenus.Add(mhdist);

            if (!edg.IsTangentialEdge())
            {
                (Axis rotationAxis1, GeoVector fromHere1, GeoVector toHere1) = ParametricsAngleAction.GetRotationAxis(edg.PrimaryFace, edg.SecondaryFace, clickBeam);
                if (rotationAxis1.IsValid)
                {
                    DirectMenuEntry rotateMenu = new DirectMenuEntry("MenuId.FaceAngle");
                    rotateMenu.ExecuteMenu = (frame) =>
                    {
                        ParametricsAngleAction pa = new ParametricsAngleAction(edg.PrimaryFace, edg.SecondaryFace, rotationAxis1, fromHere1, toHere1, edg, selectAction.Frame);
                        selectAction.Frame.SetAction(pa);
                        return true;
                    };
                    rotateMenu.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        GeoObjectList feedbackArrow = FeedbackArrow.MakeArcArrow(owningShell, rotationAxis1, fromHere1, toHere1, vw, FeedbackArrow.ArrowFlags.secondRed);
                        if (selected) feedback.Arrows.AddRange(feedbackArrow);
                        feedback.Refresh();
                        return true;
                    };
                    edgeMenus.Add(rotateMenu);
                }
                (Axis rotationAxis2, GeoVector fromHere2, GeoVector toHere2) = ParametricsAngleAction.GetRotationAxis(edg.SecondaryFace, edg.PrimaryFace, clickBeam);
                if (rotationAxis1.IsValid)
                {
                    DirectMenuEntry rotateMenu = new DirectMenuEntry("MenuId.FaceAngle");
                    rotateMenu.ExecuteMenu = (frame) =>
                    {
                        ParametricsAngleAction pa = new ParametricsAngleAction(edg.SecondaryFace, edg.PrimaryFace, rotationAxis2, fromHere2, toHere2, edg, selectAction.Frame);
                        selectAction.Frame.SetAction(pa);
                        return true;
                    };
                    rotateMenu.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        GeoObjectList feedbackArrow = FeedbackArrow.MakeArcArrow(owningShell, rotationAxis2, fromHere2, toHere2, vw, FeedbackArrow.ArrowFlags.secondRed);
                        if (selected) feedback.Arrows.AddRange(feedbackArrow);
                        feedback.Refresh();
                        return true;
                    };
                    edgeMenus.Add(rotateMenu);
                }
            }
            subEntries.Add(edgeMenus);
        }

        private void AddFaceProperties(IView vw, Face fc, Axis clickBeam)
        {
            if (!(fc.Owner is Shell)) return;
            HashSet<Face> faces = Shell.ConnectedSameGeometryFaces(new Face[] { fc }); // in case of half cylinders etc. use the whole cylinder

            // where did the user touch the face? We need this point for the display of the dimensioning arrow
            GeoPoint2D[] ips2d = fc.Surface.GetLineIntersection(clickBeam.Location, clickBeam.Direction);
            if (ips2d.Length > 1)
            {
                double minPos = double.MaxValue;
                for (int i = 0; i < ips2d.Length; i++)
                {
                    bool isInside = false;
                    foreach (Face face in faces)
                    {
                        if (face.Contains(ref ips2d[i], true)) isInside = true;
                    }
                    if (isInside)
                    {
                        GeoPoint p = fc.Surface.PointAt(ips2d[i]);
                        double pos = Geometry.LinePar(clickBeam.Location, clickBeam.Direction, p);
                        if (pos < minPos)
                        {
                            minPos = pos;
                            ips2d[0] = ips2d[i];
                        }
                    }
                }
            }
            GeoPoint touchingPoint; // the point where to attach the dimension feedback
            if (ips2d.Length == 0) touchingPoint = fc.Surface.PointAt(fc.Area.GetSomeInnerPoint());
            else touchingPoint = fc.Surface.PointAt(ips2d[0]);

            // faceEntry: a simple group entry, which contains all face modelling menus
            SelectEntry faceEntries = new SelectEntry("MenuId.Face", true); // only handles selection
            faceEntries.IsSelected = (selected, frame) =>
            {   // show the provided face and the "same geometry connected" faces as feedback
                feedback.Clear();
                if (selected) feedback.ShadowFaces.AddRange(faces.ToArray());
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };

            // select more faces: the following clicks only regard faces and adds them to the accumulatedObjects list
            DirectMenuEntry selectMoreFaces = new DirectMenuEntry("MenuId.SelectMoreFaces");
            selectMoreFaces.ExecuteMenu = (frame) =>
            {
                accumulatedObjects.AddRange(faces);
                isAccumulating = true;
                return true;
            };
            selectMoreFaces.IsSelected = (selected, frame) =>
            {   // show the provided face and the "same geometry connected" faces as feedback
                feedback.Clear();
                if (selected) feedback.FrontFaces.AddRange(faces.ToArray());
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            faceEntries.Add(selectMoreFaces);

            // add more menus for faces with specific surfaces
            faceEntries.Add(GetSurfaceSpecificSubmenus(fc, vw, touchingPoint).ToArray());

            if (fc.Owner is Shell owningShell)
            {
                // can we find a thickness or gauge in the shell?
                double thickness = owningShell.GetGauge(fc, out HashSet<Face> frontSide, out HashSet<Face> backSide);
                if (thickness != double.MaxValue && thickness > 0.0 && frontSide.Count > 0)
                {
                    DirectMenuEntry gauge = new DirectMenuEntry("MenuId.Gauge");
                    gauge.ExecuteMenu = (frame) =>
                    {
                        ParametricsOffsetAction po = new ParametricsOffsetAction(frontSide, backSide, cadFrame, thickness);
                        selectAction.Frame.SetAction(po);
                        return true;
                    };
                    gauge.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected)
                        {
                            feedback.FrontFaces.AddRange(frontSide.ToArray());
                            feedback.FrontFaces.AddRange(backSide.ToArray());
                            feedback.Arrows.AddRange(FeedbackArrow.MakeLengthArrow(owningShell, fc, backSide.First(), fc, fc.Surface.GetNormal(fc.Surface.PositionOf(touchingPoint)), touchingPoint, vw, FeedbackArrow.ArrowFlags.secondRed));
                        }
                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                        return true;
                    };
                    faceEntries.Add(gauge);
                }
                // is there a way to center these faces in the shell?
                {
                    DirectMenuEntry center = new DirectMenuEntry("MenuId.Center");
                    center.ExecuteMenu = (frame) =>
                    {
                        ParametricsCenterAction pca = new ParametricsCenterAction(faces);
                        selectAction.Frame.SetAction(pca);
                        return true;
                    };
                    center.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected) feedback.FrontFaces.AddRange(faces.ToArray());
                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                        return true;
                    };
                    faceEntries.Add(center);
                }
                // what distances on the face can we find
                int n = owningShell.GetFaceDistances(fc, touchingPoint, out List<Face> distanceTo, out List<double> distance, out List<GeoPoint> pointsFrom, out List<GeoPoint> pointsTo);
                for (int j = 0; j < n; j++)
                {
                    if (backSide == null || !backSide.Contains(distanceTo[j])) // this is not already used as gauge
                    {
                        HashSet<Face> capturedFaceI = new HashSet<Face>(new Face[] { fc });
                        HashSet<Face> capturedDistTo = new HashSet<Face>(new Face[] { distanceTo[j] });
                        double capturedDistance = distance[j];
                        GeoPoint capturedPoint1 = pointsFrom[j];
                        GeoPoint capturedPoint2 = pointsTo[j];
                        DirectMenuEntry faceDist = new DirectMenuEntry("MenuId.FaceDistance");
                        faceDist.ExecuteMenu = (frame) =>
                        {
                            ParametricsDistanceAction pd = new ParametricsDistanceAction(capturedDistTo, capturedFaceI, capturedPoint2, capturedPoint1, touchingPoint, selectAction.Frame);
                            selectAction.Frame.SetAction(pd);
                            return true;
                        };
                        faceDist.IsSelected = (selected, frame) =>
                        {
                            feedback.Clear();
                            if (selected)
                            {
                                feedback.FrontFaces.AddRange(capturedFaceI.ToArray());
                                feedback.BackFaces.AddRange(capturedDistTo.ToArray());
                                feedback.Arrows.AddRange(FeedbackArrow.MakeLengthArrow(owningShell, capturedFaceI.First(), capturedDistTo.First(), fc,
                                    capturedPoint2 - capturedPoint1, touchingPoint, vw, FeedbackArrow.ArrowFlags.secondRed));
                            }
                            feedback.Refresh();
                            return true;
                        };
                        faceEntries.Add(faceDist);
                    }
                }
                // Check for possibilities to rotate this face.
                List<DirectMenuEntry> rotateMenus = new List<DirectMenuEntry>();
                if (fc.Surface is PlaneSurface ps)
                {
                    foreach (Edge edge in fc.OutlineEdges)
                    {
                        if (!edge.IsTangentialEdge())
                        {
                            Face capturedFace = fc;
                            Face otherFace = edge.OtherFace(fc);
                            if (otherFace != null)
                            {
                                (Axis rotationAxis, GeoVector fromHere, GeoVector toHere) = ParametricsAngleAction.GetRotationAxis(fc, otherFace, clickBeam);
                                if (rotationAxis.IsValid)
                                {
                                    DirectMenuEntry rotateMenu = new DirectMenuEntry("MenuId.FaceAngle");
                                    rotateMenu.ExecuteMenu = (frame) =>
                                    {
                                        ParametricsAngleAction pa = new ParametricsAngleAction(capturedFace, otherFace, rotationAxis, fromHere, toHere, edge, selectAction.Frame);
                                        selectAction.Frame.SetAction(pa);
                                        return true;
                                    };
                                    rotateMenu.IsSelected = (selected, frame) =>
                                    {
                                        feedback.Clear();
                                        GeoObjectList feedbackArrow = FeedbackArrow.MakeArcArrow(owningShell, rotationAxis, fromHere, toHere, vw, FeedbackArrow.ArrowFlags.secondRed);
                                        if (selected)
                                        {
                                            feedback.Arrows.AddRange(feedbackArrow);
                                        }
                                        feedback.Refresh();
                                        return true;
                                    };
                                    rotateMenus.Add(rotateMenu);
                                }
                            }
                        }
                    }
                }
                if (rotateMenus.Count > 0)
                {
                    if (rotateMenus.Count == 1) faceEntries.Add(rotateMenus[0]);
                    else
                    {
                        SimplePropertyGroup rm = new SimplePropertyGroup("MenuId.FacesAngle");
                        rm.Add(rotateMenus.ToArray());
                        faceEntries.Add(rm);
                    }
                }
                // else: more to come!
            }
            subEntries.Add(faceEntries);
        }
        private List<IPropertyEntry> AddExtensionProperties(Face fc, Projection.PickArea pa, IView vw)
        {
            List<IPropertyEntry> res = new List<IPropertyEntry>();
            if (!(fc.Owner is Shell)) return res;
            FindExtrusionLoops(fc, pa, out List<ICurve[]> loopCurves, out List<Face[]> loopFaces, out List<Edge[]> loopEdges, out List<Plane> loopPlanes, out List<SimpleShape> loopShapes, out GeoPoint pointOnFace);
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
                // GeoObjectList feedbackArrow = vw.Projection.MakeArrow(zmin, zmax, arrowPlane, Projection.ArrowMode.circleArrow);
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
                DirectMenuEntry extrudeMenu = new DirectMenuEntry("MenuId.ExtrusionLength");
                Face[] lfaces = loopFaces[i]; // captured values
                Edge[] ledges = loopEdges[i];
                Plane lplane = loopPlanes[i];
                extrudeMenu.IsSelected = (selected, frame) =>
                {
                    if (selected)
                    {
                        GeoObjectList feedbackArrow = FeedbackArrow.MakeLengthArrow(fc.Owner as Shell, minObject, maxObject, fc, lplane.Normal, pointOnFace, vw);
                        feedback.Clear();
                        feedback.SelectedObjects.Add(extrFace);
                        feedback.Arrows.AddRange(feedbackArrow);
                        feedback.Refresh();
                    }
                    return true;
                };
                extrudeMenu.ExecuteMenu = (menuId) =>
                {
                    GeoVector crossDir = lplane.Normal ^ fc.Surface.GetNormal(fc.PositionOf(pointOnFace));
                    Face arrow1 = FeedbackArrow.MakeSimpleTriangle(pointOnFace, lplane.Normal, crossDir, vw.Projection);
                    Face arrow2 = FeedbackArrow.MakeSimpleTriangle(pointOnFace, -lplane.Normal, -crossDir, vw.Projection);
                    ParametricsExtrudeAction pea = new ParametricsExtrudeAction(minObject, maxObject, lfaces, ledges, lplane, extrFace,
                        pointOnFace, new Face[] { arrow1, arrow2 }, cadFrame);
                    selectAction.Frame.SetAction(pea);
                    return true;
                };
                res.Add(extrudeMenu);
            }
            return res;
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
            Dictionary<Edge, Edge> clonedEdges = new Dictionary<Edge, Edge>();
            Dictionary<Vertex, Vertex> clonedVertices = new Dictionary<Vertex, Vertex>();
            List<Face> ff = new List<Face>(featureFaces.Select(fc => fc.Clone(clonedEdges, clonedVertices) as Face));
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
                    feedback.Clear();
                    feedback.FrontFaces.Add(feature);
                    vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                }
            };

            DirectMenuEntry copyFeature = new DirectMenuEntry("MenuId.Feature.CopyToClipboard");
            DirectMenuEntry positionFeature = new DirectMenuEntry("MenuId.Feature.Position");
            DirectMenuEntry nameFeature = new DirectMenuEntry("MenuId.Feature.Name");
            DirectMenuEntry removeFeature = new DirectMenuEntry("MenuId.Feature.Remove");
            DirectMenuEntry splitFeature = new DirectMenuEntry("MenuId.Feature.Split");
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
                feedback.Clear();
                feedback.FrontFaces.Add(feature);
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
                feedback.Clear();
                feedback.FrontFaces.Add(feature);
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
                feedback.Clear();
                feedback.FrontFaces.Add(feature);
                vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                return true;
            };
            removeFeature.ExecuteMenu = (frame) =>
            {
                bool addRemoveOk = shell.AddAndRemoveFaces(connection, featureFaces);
                feedback.Clear();
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
            out List<Edge[]> loopEdges, out List<Plane> loopPlanes, out List<SimpleShape> loopShapes, out GeoPoint pointOnFace)
        {
            loopCurves = new List<ICurve[]>();
            loopFaces = new List<Face[]>();
            loopEdges = new List<Edge[]>();
            loopPlanes = new List<Plane>();
            loopShapes = new List<SimpleShape>();
            pointOnFace = GeoPoint.Invalid;
            List<Plane> planeCandidates = new List<Plane>();
            List<Edge> edgeCandidates = new List<Edge>();
            // find the point, which was hit by the mouse
            GeoPoint[] ips = fc.GetLineIntersection(pa.FrontCenter, pa.Direction);
            if (ips.Length > 0)
            {
                pointOnFace = ips.MinBy(p => (p | pa.FrontCenter)); // this is the point on the face, defined by the mouse position (and closest to the viewer)
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
        /// <summary>
        /// Will be called when the DrawingAspect.Select of the view has to be repainted (and modellingIsActive).
        /// </summary>
        /// <param name="IsInvalid"></param>
        /// <param name="View"></param>
        /// <param name="PaintToSelect"></param>
        private bool CanMakePath(List<ICurve> curves)
        {
            List<GeoPoint> points = new List<GeoPoint>();
            for (int i = 0; i < curves.Count; i++)
            {
                if (!curves[i].IsClosed)
                {
                    for (int j = 0; j < points.Count; j++)
                    {
                        if (Precision.IsEqual(curves[i].StartPoint, points[j])) return true;
                        if (Precision.IsEqual(curves[i].EndPoint, points[j])) return true;
                    }
                    points.Add(curves[i].StartPoint);
                    points.Add(curves[i].EndPoint);
                }
            }
            return false;
        }
        internal void OnProjectionChanged()
        {
            IPropertyEntry selection = propertyPage?.GetCurrentSelection();
            if (selection != null)
            {
                selection.Selected(selection); // to regenerate the feedback display
                // and by passing selected as "previousSelected" parameter, they can only regenerate projection dependant feedback 
            }
        }

        private List<IPropertyEntry> GetSurfaceSpecificSubmenus(Face face, IView vw, GeoPoint touchingPoint)
        {
            Shell shell = face.Owner as Shell;
            List<IPropertyEntry> res = new List<IPropertyEntry>();
            if (shell == null) return res;
            if (face.IsFillet())
            {
                // handling of a fillet: change radius or remove fillet, composed to a group
                HashSet<Face> connectedFillets = new HashSet<Face>();
                CollectConnectedFillets(face, connectedFillets);
                SelectEntry fch = new SelectEntry("MenuId.Fillet", true);
                fch.IsSelected = (selected, frame) =>
                {
                    feedback.Clear();
                    if (selected) feedback.FrontFaces.AddRange(connectedFillets.ToArray());
                    vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                    return true;
                };
                DirectMenuEntry fr = new DirectMenuEntry("MenuId.Fillet.ChangeRadius");
                fr.ExecuteMenu = (frame) =>
                {
                    ParametricsRadiusAction pr = new ParametricsRadiusAction(connectedFillets.ToArray(), selectAction.Frame, true, touchingPoint);
                    selectAction.Frame.SetAction(pr);
                    return true;
                };
                fr.IsSelected = (selected, frame) =>
                {
                    feedback.Clear();
                    if (selected) feedback.FrontFaces.AddRange(connectedFillets.ToArray());
                    vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                    return true;
                };
                DirectMenuEntry fd = new DirectMenuEntry("MenuId.Fillet.Remove");
                fd.ExecuteMenu = (frame) =>
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
                    return true;
                };
                fd.IsSelected = (selected, frame) =>
                {
                    feedback.Clear();
                    if (selected) feedback.FrontFaces.AddRange(connectedFillets.ToArray());
                    vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                    return true;
                };
                fch.Add(new IPropertyEntry[] { fr, fd });
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
                    DirectMenuEntry mh = new DirectMenuEntry("MenuId.FeatureDiameter");
                    mh.ExecuteMenu = (frame) =>
                    {
                        ParametricsRadiusAction pr = new ParametricsRadiusAction(lconnected.ToArray(), selectAction.Frame, false, touchingPoint);
                        selectAction.Frame.SetAction(pr);
                        return true;
                    };
                    mh.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected)
                        {
                            feedback.ShadowFaces.AddRange(lconnected.ToArray());
                            GeoObjectList arrow = FeedbackArrow.MakeLengthArrow(shell, lconnected.First(), lconnected.Last(), null, GeoVector.NullVector, touchingPoint, vw, FeedbackArrow.ArrowFlags.firstRed | FeedbackArrow.ArrowFlags.secondRed);
                            feedback.Arrows.AddRange(arrow);
                        }
                        feedback.Refresh();
                        return true;
                    };
                    res.Add(mh);
                }
                if (face.Surface is CylindricalSurface || face.Surface is CylindricalSurfaceNP || face.Surface is ConicalSurface)
                {
                    Line axis = null;

                    if (face.Surface is ICylinder cyl) axis = cyl.Axis.Clip(ext);
                    if (face.Surface is ConicalSurface cone) axis = cone.AxisLine(face.Domain.Bottom, face.Domain.Top);
                    DirectMenuEntry mh = new DirectMenuEntry("MenuId.AxisPosition");
                    mh.ExecuteMenu = (frame) =>
                    {
                        ParametricsDistanceAction pd = new ParametricsDistanceAction(lconnected, axis, selectAction.Frame);
                        selectAction.Frame.SetAction(pd);
                        return true;
                    };
                    mh.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected) feedback.FrontFaces.AddRange(lconnected.ToArray());
                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                        return true;
                    };
                    res.Add(mh);
                }
            }
            if (face.Surface is PlaneSurface pls)
            {
                // set this planar face as drawing plane
                DirectMenuEntry dp = new DirectMenuEntry("MenuId.SetDrawingPlane");
                dp.ExecuteMenu = (frame) => { vw.Projection.DrawingPlane = pls.Plane; return true; };
                dp.IsSelected = (selected, frame) =>
                    {
                        feedback.Clear();
                        if (selected) feedback.FrontFaces.Add(face);
                        vw.Invalidate(PaintBuffer.DrawingAspect.Select, vw.DisplayRectangle);
                        return true;
                    };
                res.Add(dp);
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
                                Edge o1 = outline[j]; // capture the two edges
                                Edge o2 = outline[k];
                                (GeoPoint p1, GeoPoint p2) = DistanceCalculator.DistanceBetweenObjects(l1, l2, pls.Normal ^ l1.StartDirection, touchingPoint, out GeoVector _, out GeoPoint dp1, out GeoPoint dp2);
                                if ((p1 | p2) > Precision.eps)
                                {
                                    DirectMenuEntry mh = new DirectMenuEntry("MenuId.EdgeDistance");
                                    mh.ExecuteMenu = (frame) =>
                                    {
                                        ParametricsDistanceAction pd = new ParametricsDistanceAction(new Face[] { o2.OtherFace(face) }, new Face[] { o1.OtherFace(face) }, p2, p1, touchingPoint, cadFrame);
                                        cadFrame.SetAction(pd);
                                        return true;
                                    };
                                    mh.IsSelected = (selected, frame) =>
                                    {
                                        feedback.Clear();
                                        if (selected)
                                        {
                                            GeoObjectList fb = FeedbackArrow.MakeLengthArrow(shell, o1.Curve3D, o2.Curve3D, face, pls.Normal ^ l1.StartDirection, touchingPoint, vw, FeedbackArrow.ArrowFlags.secondRed);
                                            feedback.Arrows.AddRange(fb);
                                            feedback.FrontFaces.Add(o1.OtherFace(face));
                                            feedback.BackFaces.Add(o2.OtherFace(face));
                                        }
                                        feedback.Refresh();
                                        return true;
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
                List<IPropertyEntry> lm = new List<IPropertyEntry>();
                for (int i = 4; i < res.Count; i++)
                {
                    lm.Add(res[i]);
                }
                res.RemoveRange(4, lm.Count);
                SimplePropertyGroup subMenu = new SimplePropertyGroup("MenuId.More");
                subMenu.Add(lm.ToArray());
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

    }
}
