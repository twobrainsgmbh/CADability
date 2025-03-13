using CADability.GeoObject;
using CADability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using CADability.Attribute;

namespace ShapeIt
{
    internal class Feedback
    {
        private IView view;
        public GeoObjectList FrontFaces = new GeoObjectList(); // List of front faces for distance for feedback
        public GeoObjectList BackFaces = new GeoObjectList(); // List of back faces for distance for feedback
        public GeoObjectList SelectedObjects = new GeoObjectList(); // List of selected objects to be displayed, when a entry is selected
        public GeoObjectList Arrows = new GeoObjectList(); // List of highlighted objects to be displayed, when a entry is selected
        private IPaintTo3DList frontFacesDisplayList = null;
        private IPaintTo3DList backFacesDisplayList = null;
        private IPaintTo3DList selectedObjectsDisplayList = null;
        private IPaintTo3DList arrowsDisplayList = null;

        ColorDef frontColor, backColor, selectColor;
        public Feedback()
        {
            frontColor = new ColorDef("frontColor", Color.Yellow);
            backColor = new ColorDef("backColor", Color.LightBlue);
            selectColor = new ColorDef("frontColor", Color.LightPink);
        }
        public void Attach(IView vw)
        {
            if (view != null) Detach();
            view = vw;
            vw.SetPaintHandler(PaintBuffer.DrawingAspect.Select, OnRepaint);
        }
        public void Detach()
        {
            view.RemovePaintHandler(PaintBuffer.DrawingAspect.Select, OnRepaint);
        }

        public void Clear()
        {
            FrontFaces.Clear();
            BackFaces.Clear();
            SelectedObjects.Clear();
            Arrows.Clear();
            // reset the display lists, so they will be recreated when the next OnRepaintSelect is called
            frontFacesDisplayList = null;
            backFacesDisplayList = null;
            selectedObjectsDisplayList = null;
            arrowsDisplayList = null;
        }

        public void Refresh()
        {
            view.Invalidate(PaintBuffer.DrawingAspect.Select, view.DisplayRectangle);
        }

        private void OnRepaint(Rectangle IsInvalid, IView view, IPaintTo3D PaintToSelect)
        {
            // save the state of PaintToSelect
            PaintToSelect.PushState();
            bool oldSelect = PaintToSelect.SelectMode;
            bool pse = PaintToSelect.PaintSurfaceEdges;

            PaintToSelect.UseZBuffer(true);
            PaintToSelect.Blending(true);
            // if the display lists are null, regenerate them
            // PaintToSelect.PaintSurfaceEdges = false;

            if (selectedObjectsDisplayList == null)
            {
                PaintToSelect.OpenList("selected-objects");
                PaintToSelect.SetColor(Color.FromArgb(0, selectColor.Color)); // switch on color override with this color
                PaintToSelect.SetColor(Color.FromArgb(0, selectColor.Color));
                foreach (IGeoObject go in SelectedObjects)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                selectedObjectsDisplayList = PaintToSelect.CloseList();
                PaintToSelect.SetColor(Color.FromArgb(1, selectColor.Color)); // switch off color override
                PaintToSelect.SetColor(Color.FromArgb(1, selectColor.Color));
            }
            // PaintToSelect.SelectMode = false;
            // front and back faces list in plain mode
            PaintToSelect.SetColor(Color.Yellow);
            if (frontFacesDisplayList == null)
            {
                PaintToSelect.OpenList("front-faces");
                PaintToSelect.SetColor(Color.FromArgb(0, frontColor.Color));
                PaintToSelect.SetColor(Color.FromArgb(0, frontColor.Color));
                foreach (IGeoObject go in FrontFaces)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                frontFacesDisplayList = PaintToSelect.CloseList();
                PaintToSelect.SetColor(Color.FromArgb(1, frontColor.Color));
                PaintToSelect.SetColor(Color.FromArgb(1, frontColor.Color));
            }
            PaintToSelect.SetColor(Color.LightBlue);
            if (backFacesDisplayList == null)
            {
                PaintToSelect.OpenList("back-faces");
                PaintToSelect.SetColor(Color.FromArgb(0,backColor.Color));
                PaintToSelect.SetColor(Color.FromArgb(0,backColor.Color));
                foreach (IGeoObject go in BackFaces)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                backFacesDisplayList = PaintToSelect.CloseList();
                PaintToSelect.SetColor(Color.FromArgb(1, backColor.Color));
                PaintToSelect.SetColor(Color.FromArgb(1, backColor.Color));
            }
            PaintToSelect.SetColor(Color.Black); // color to display the arrows an text. objects should have ColorDef==null, so they don't set the color
            bool oldTriangulateText = PaintToSelect.TriangulateText;
            PaintToSelect.TriangulateText = false;
            if (arrowsDisplayList == null)
            {   // no color override, we use actual colors of the objects
                PaintToSelect.OpenList("arrow-objects");
                PaintToSelect.SetColor(Color.Black); // in case of no color specified
                foreach (IGeoObject go in Arrows)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                arrowsDisplayList = PaintToSelect.CloseList();
            }

            // show the display lists            
            // a small amount to the front
            ModOp toViewer = ModOp.Translate(-2 * PaintToSelect.Precision * view.Projection.Direction.x, -2 * PaintToSelect.Precision * view.Projection.Direction.y, -2 * PaintToSelect.Precision * view.Projection.Direction.z);
            PaintToSelect.PushMultModOp(toViewer);
            if (frontFacesDisplayList != null) PaintToSelect.List(frontFacesDisplayList);
            if (backFacesDisplayList != null) PaintToSelect.List(backFacesDisplayList);
            PaintToSelect.SelectMode = true;
            if (selectedObjectsDisplayList != null) PaintToSelect.SelectedList(selectedObjectsDisplayList, 6);// width of the brim
            PaintToSelect.SelectMode = false;
            PaintToSelect.PopModOp();
            // even more to the front to clearly show the domension lines
            toViewer = ModOp.Translate(-4 * PaintToSelect.Precision * view.Projection.Direction.x, -4 * PaintToSelect.Precision * view.Projection.Direction.y, -4 * PaintToSelect.Precision * view.Projection.Direction.z);
            PaintToSelect.PushMultModOp(toViewer);
            if (arrowsDisplayList != null) PaintToSelect.List(arrowsDisplayList);

            // restore the state of PaintToSelect
            PaintToSelect.PopModOp();
            PaintToSelect.TriangulateText = oldTriangulateText;
            PaintToSelect.SelectMode = oldSelect;
            PaintToSelect.PaintSurfaceEdges = pse;
            PaintToSelect.PopState();
        }
    }
}
